using System;
using System.Collections.Generic;
using Eden.Domain;
using Eden.Simulation;
using UnityEngine;

namespace Eden.Presentation
{
    /// <summary>
    /// Draws the world with pooled sprites: arena, obstacles, food and creatures (body sized by its eat radius and
    /// colored by the share of its own capacity it holds, a nose dot showing heading, a ring and the sensor range on the
    /// selected one; for a selected creature with eyes, each eye's cone instead, with a dot on the food each eye sees
    /// now). With solid bodies the body is the solid disc itself, the selected one also shows its mouth's reach as a
    /// halo, and a dot marks where two bodies touch now (<see cref="PhysicalInspection"/>). Creatures
    /// are drawn between their pose before the last tick (see
    /// <see cref="RememberPreviousTick"/>) and their current pose, so motion is smooth at any frame rate although ticks
    /// run at 20 Hz. Read-only: it never changes simulation state and never draws from simulation random streams.
    /// Arena units map 1:1 to Unity world units.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        private const float CreatureDiameter = 1f;
        private const float FoodDiameter = 0.4f;
        private const float CameraMargin = 1.08f;

        private static readonly Color ArenaColor = new Color32(22, 40, 46, 255);
        private static readonly Color FoodColor = new Color32(240, 200, 92, 255);
        private static readonly Color HungryColor = new Color32(217, 84, 64, 255);
        private static readonly Color FedColor = new Color32(94, 214, 168, 255);
        private static readonly Color NoseColor = new Color32(14, 26, 31, 255);
        private static readonly Color SelectionColor = Color.white;
        private static readonly Color SensorColor = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color TargetColor = Color.white;
        private static readonly Color ObstacleColor = new Color32(74, 88, 96, 255);
        private static readonly Color WaterColor = new Color32(58, 120, 196, 255);
        private static readonly Color ReachColor = new Color32(240, 200, 92, 70);
        private static readonly Color ContactColor = new Color32(255, 150, 60, 255);
        private const float ContactDiameter = 0.22f;
        private const float SolidSelectionScale = 1.2f;
        private const float PointSelectionScale = 1.5f;
        private const int ConeSegments = 16;
        private const float TargetDiameter = 0.55f;

        // One tint per eye slot, so an eye keeps its color whatever it points at.
        private static readonly Color32[] EyeColors =
        {
            new Color32(94, 214, 168, 46), new Color32(120, 170, 255, 46), new Color32(255, 190, 90, 46),
            new Color32(225, 120, 225, 46)
        };

        [SerializeField] private Camera targetCamera;
        [SerializeField] private Sprite circleSprite;
        [SerializeField] private Sprite squareSprite;
        [SerializeField] private Material spriteMaterial;

        private readonly Dictionary<CreatureId, CreatureVisual> creatures = new Dictionary<CreatureId, CreatureVisual>();
        private readonly Dictionary<FoodId, FoodVisual> food = new Dictionary<FoodId, FoodVisual>();
        private readonly Stack<CreatureVisual> creaturePool = new Stack<CreatureVisual>();
        private readonly Stack<FoodVisual> foodPool = new Stack<FoodVisual>();
        private readonly List<CreatureId> staleCreatures = new List<CreatureId>();
        private readonly List<FoodId> staleFood = new List<FoodId>();
        private readonly Dictionary<CreatureId, Pose> previous = new Dictionary<CreatureId, Pose>();
        private WorldState previousWorld;
        private long previousTick = -1;
        private SpriteRenderer arena;
        private int frame;
        private MeshFilter eyeCones;
        private Mesh eyeMesh;
        private readonly List<Vector3> coneVertices = new List<Vector3>();
        private readonly List<Color32> coneColors = new List<Color32>();
        private readonly List<int> coneTriangles = new List<int>();
        private readonly SpriteRenderer[] eyeTargets = new SpriteRenderer[EyeSet.MaxEyes];
        private readonly float[] eyeReading = new float[EyeChannels.Count];
        private readonly List<SpriteRenderer> obstacles = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> contactDots = new List<SpriteRenderer>();
        private readonly List<TouchingPair> contactPairs = new List<TouchingPair>();
        private readonly List<Vector2> drawnPositions = new List<Vector2>();
        private readonly PhysicalInspection inspection = new PhysicalInspection();

        /// <summary>Eye cones drawn in the last <see cref="Render"/> (those of the selected creature).</summary>
        public int DrawnEyeCones { get; private set; }

        /// <summary>Food items marked as seen by the selected creature's eyes in the last <see cref="Render"/>.</summary>
        public int DrawnEyeTargets { get; private set; }

        /// <summary>Obstacles drawn for the bound world.</summary>
        public int DrawnObstacles { get; private set; }

        /// <summary>Water sources drawn for the bound world.</summary>
        public int DrawnWater { get; private set; }

        /// <summary>Touching pairs of solid bodies marked in the last <see cref="Render"/>.</summary>
        public int DrawnContacts { get; private set; }

        public int VisibleCreatures => creatures.Count;
        public int VisibleFood => food.Count;

        /// <summary>Fraction of the way from the previous to the current pose used by the last <see cref="Render"/>.</summary>
        public float LastFraction { get; private set; } = 1f;

        /// <summary>
        /// Keeps every creature's pose before a tick runs; called before the last tick of a frame. The next renders
        /// draw from these poses toward the state after that tick.
        /// </summary>
        public void RememberPreviousTick(WorldState world)
        {
            previous.Clear();
            previousWorld = world;
            previousTick = world.Tick;
            for (var i = 0; i < world.Creatures.Count; i++)
            {
                var creature = world.Creatures[i];
                previous[creature.Id] = new Pose(creature.Position.X, creature.Position.Y, creature.Heading);
            }
        }

        /// <summary>Remembered pose of a creature for the current tick's interpolation, if there is one.</summary>
        public bool TryGetPreviousPose(WorldState world, CreatureId creature, out Vector2 position, out float headingRadians)
        {
            if (HasPrevious(world) && previous.TryGetValue(creature, out var pose))
            {
                position = new Vector2(pose.X, pose.Y);
                headingRadians = pose.Heading;
                return true;
            }

            position = default;
            headingRadians = 0f;
            return false;
        }

        /// <summary>
        /// Size the creature is drawn at relative to the reference body, and the diameter of its sensor disk in arena
        /// units (0 unless it is selected).
        /// </summary>
        public bool TryGetDrawnBody(CreatureId creature, out float scale, out float sensorDiameter)
        {
            if (creatures.TryGetValue(creature, out var visual))
            {
                scale = visual.Root.transform.localScale.x;
                sensorDiameter = visual.Sensor.enabled ? visual.Sensor.transform.localScale.x * scale : 0f;
                return true;
            }

            scale = 0f;
            sensorDiameter = 0f;
            return false;
        }

        /// <summary>
        /// Diameter in arena units of the mouth's reach drawn around the selected solid body; false when none is drawn
        /// for that creature (not selected, or points instead of solid bodies, where the body already is the reach).
        /// </summary>
        public bool TryGetDrawnReach(CreatureId creature, out float diameter)
        {
            if (creatures.TryGetValue(creature, out var visual) && visual.Reach.enabled)
            {
                diameter = visual.Reach.transform.localScale.x * visual.Root.transform.localScale.x;
                return true;
            }

            diameter = 0f;
            return false;
        }

        /// <summary>Where a creature was last drawn, in arena units and degrees.</summary>
        public bool TryGetDrawnPose(CreatureId creature, out Vector2 position, out float headingDegrees)
        {
            if (creatures.TryGetValue(creature, out var visual))
            {
                var drawn = visual.Root.transform;
                position = drawn.localPosition;
                headingDegrees = drawn.localEulerAngles.z;
                return true;
            }

            position = default;
            headingDegrees = 0f;
            return false;
        }

        /// <summary>Resets visuals for a new world and fits the camera to its arena.</summary>
        public void Bind(WorldState world)
        {
            foreach (var visual in creatures.Values)
                Release(visual);
            creatures.Clear();
            foreach (var visual in food.Values)
                Release(visual);
            food.Clear();
            previous.Clear();
            previousWorld = null;

            var config = world.Config;
            if (arena == null)
                arena = CreateRenderer("Arena", transform, squareSprite, ArenaColor, -10);
            arena.transform.localPosition = new Vector3(config.ArenaWidth / 2f, config.ArenaHeight / 2f, 0f);
            arena.transform.localScale = new Vector3(config.ArenaWidth, config.ArenaHeight, 1f);
            BindObstacles(config);

            if (targetCamera != null)
            {
                targetCamera.orthographic = true;
                targetCamera.orthographicSize =
                    Mathf.Max(config.ArenaHeight / 2f, config.ArenaWidth / 2f / targetCamera.aspect) * CameraMargin;
                targetCamera.transform.position = new Vector3(config.ArenaWidth / 2f, config.ArenaHeight / 2f, -10f);
            }

            Render(world, null);
        }

        /// <summary>
        /// Draws <paramref name="world"/>; creatures remembered just before the last tick are drawn
        /// <paramref name="fraction"/> of the way from that pose to the current one (1 draws the current state).
        /// </summary>
        public void Render(WorldState world, CreatureId? selected, float fraction = 1f)
        {
            frame++;
            var drewEyes = false;
            var referenceRadius = world.Config.EatRadius;
            var solid = world.Config.SolidBodies;
            drawnPositions.Clear();
            var interpolate = fraction < 1f && HasPrevious(world);
            LastFraction = interpolate ? Mathf.Clamp01(fraction) : 1f;
            // Indexed loops: a foreach over the read-only lists would allocate an enumerator every frame.
            for (var i = 0; i < world.Creatures.Count; i++)
            {
                var creature = world.Creatures[i];
                if (!creatures.TryGetValue(creature.Id, out var visual))
                {
                    visual = creaturePool.Count > 0 ? creaturePool.Pop() : CreateCreatureVisual();
                    visual.Root.SetActive(true);
                    creatures.Add(creature.Id, visual);
                }

                visual.Frame = frame;
                var x = creature.Position.X;
                var y = creature.Position.Y;
                var heading = creature.Heading * Mathf.Rad2Deg;
                if (interpolate && previous.TryGetValue(creature.Id, out var before))
                {
                    x = Mathf.Lerp(before.X, x, LastFraction);
                    y = Mathf.Lerp(before.Y, y, LastFraction);
                    heading = Mathf.LerpAngle(before.Heading * Mathf.Rad2Deg, heading, LastFraction);
                }

                visual.Root.transform.localPosition = new Vector3(x, y, 0f);
                visual.Root.transform.localRotation = Quaternion.Euler(0f, 0f, heading);
                drawnPositions.Add(new Vector2(x, y));
                var body = creature.Body;
                // Solid bodies are drawn as the disc that collides; points as the reach of their mouth.
                var size = solid ? 2f * body.Radius / CreatureDiameter : body.EatRadius / referenceRadius;
                visual.Root.transform.localScale = new Vector3(size, size, 1f);
                visual.Body.color = Color.Lerp(HungryColor, FedColor, Mathf.Clamp01(creature.Energy / body.MaxEnergy));
                var isSelected = selected.HasValue && selected.Value == creature.Id;
                visual.Selection.enabled = isSelected;
                visual.Selection.transform.localScale =
                    Vector3.one * CreatureDiameter * (solid ? SolidSelectionScale : PointSelectionScale);
                visual.Reach.enabled = isSelected && solid;
                if (isSelected && solid)
                    visual.Reach.transform.localScale = Vector3.one * (2f * body.EatRadius / size);
                visual.Sensor.enabled = isSelected && creature.Eyes == null;
                if (isSelected && creature.Eyes == null)
                    visual.Sensor.transform.localScale = Vector3.one * (2f * body.SensorRange / size);
                if (isSelected && creature.Eyes != null)
                {
                    DrawEyes(world, creature, x, y, heading * Mathf.Deg2Rad);
                    drewEyes = true;
                }
            }

            if (!drewEyes)
                HideEyes();
            DrawContacts(world);

            for (var i = 0; i < world.Food.Count; i++)
            {
                var item = world.Food[i];
                if (!food.TryGetValue(item.Id, out var visual))
                {
                    visual = foodPool.Count > 0 ? foodPool.Pop() : CreateFoodVisual();
                    visual.Renderer.gameObject.SetActive(true);
                    food.Add(item.Id, visual);
                }

                visual.Frame = frame;
                visual.Renderer.transform.localPosition = new Vector3(item.Position.X, item.Position.Y, 0f);
            }

            RemoveStale();
        }

        // The obstacles and water sources of the bound arena (config.Barriers: obstacles first): static, so drawn once per
        // world.
        private void BindObstacles(SimulationConfig config)
        {
            var count = config.Barriers.Count;
            while (obstacles.Count < count)
                obstacles.Add(CreateRenderer("Obstacle", transform, squareSprite, ObstacleColor, -5));
            for (var i = 0; i < obstacles.Count; i++)
            {
                var shown = i < count;
                obstacles[i].gameObject.SetActive(shown);
                if (!shown)
                    continue;
                var obstacle = config.Barriers[i];
                obstacles[i].color = i < config.Obstacles.Count ? ObstacleColor : WaterColor;
                obstacles[i].transform.localPosition = new Vector3((obstacle.MinX + obstacle.MaxX) / 2f,
                    (obstacle.MinY + obstacle.MaxY) / 2f, 0f);
                obstacles[i].transform.localScale = new Vector3(obstacle.Width, obstacle.Height, 1f);
            }

            DrawnObstacles = config.Obstacles.Count;
            DrawnWater = config.Water.Count;
        }

        /// <summary>
        /// Shows which water targets of a search episode were reached (dimmed); food targets disappear when reached.
        /// The bound world must be the episode's (its water sources are the ponds, in target order).
        /// </summary>
        public void ShowReachedWater(SearchLayout layout, Func<int, bool> reached)
        {
            if (layout == null)
                return;
            for (var w = 0; w < layout.Water.Count; w++)
            {
                var index = DrawnObstacles + w;
                if (index < obstacles.Count)
                    obstacles[index].color = reached(layout.Food.Count + w) ? ReachedWaterColor : WaterColor;
            }
        }

        private static readonly Color ReachedWaterColor = new Color32(58, 120, 196, 90);

        // A dot where each touching pair meets, between where the two are drawn (the point on the line between the
        // centres at the first body's radius); no dots without solid bodies.
        private void DrawContacts(WorldState world)
        {
            var count = inspection.FindPairs(world, contactPairs);
            while (contactDots.Count < count)
            {
                var dot = CreateRenderer("Contact", transform, circleSprite, ContactColor, 5);
                dot.transform.localScale = Vector3.one * ContactDiameter;
                contactDots.Add(dot);
            }

            for (var i = 0; i < contactDots.Count; i++)
            {
                var shown = i < count;
                if (contactDots[i].enabled != shown)
                    contactDots[i].enabled = shown;
                if (!shown)
                    continue;
                var pair = contactPairs[i];
                var first = world.Creatures[pair.First].Body.Radius;
                var second = world.Creatures[pair.Second].Body.Radius;
                var at = Vector2.Lerp(drawnPositions[pair.First], drawnPositions[pair.Second], first / (first + second));
                contactDots[i].transform.localPosition = new Vector3(at.x, at.y, 0f);
            }

            DrawnContacts = count;
        }

        // The selected creature's cones, from where it is drawn, and a dot on the food each eye sees in the current state.
        private void DrawEyes(WorldState world, CreatureState creature, float x, float y, float heading)
        {
            EnsureEyeObjects();
            coneVertices.Clear();
            coneColors.Clear();
            coneTriangles.Clear();
            DrawnEyeCones = 0;
            DrawnEyeTargets = 0;
            var eyes = creature.Eyes;
            var radius = creature.Body.EatRadius;
            for (var slot = 0; slot < EyeSet.MaxEyes; slot++)
            {
                eyeTargets[slot].enabled = false;
                if (!eyes.Has(slot))
                    continue;
                var eye = eyes[slot];
                var axis = heading + eye.Orientation;
                var origin = new Vector3(x + radius * Mathf.Cos(axis), y + radius * Mathf.Sin(axis), 0f);
                var range = creature.Body.SensorRange * eye.Range;
                var first = coneVertices.Count;
                coneVertices.Add(origin);
                coneColors.Add(EyeColors[slot]);
                for (var k = 0; k <= ConeSegments; k++)
                {
                    var angle = axis - eye.Aperture * 0.5f + eye.Aperture * k / ConeSegments;
                    coneVertices.Add(origin + new Vector3(range * Mathf.Cos(angle), range * Mathf.Sin(angle), 0f));
                    coneColors.Add(EyeColors[slot]);
                    if (k == 0)
                        continue;
                    coneTriangles.Add(first);
                    coneTriangles.Add(first + k);
                    coneTriangles.Add(first + k + 1);
                }

                DrawnEyeCones++;
                Vision.See(world, creature, eyes, slot, eyeReading, 0);
                if (eyeReading[(int)EyeChannel.FoodVisible] <= 0f)
                    continue;
                var at = Vision.EyePosition(world.Config, creature, eye);
                var distance = range * (1f - eyeReading[(int)EyeChannel.FoodProximity]);
                var direction = creature.Heading + Mathf.Atan2(eyeReading[(int)EyeChannel.FoodSin],
                    eyeReading[(int)EyeChannel.FoodCos]);
                eyeTargets[slot].transform.localPosition = new Vector3(at.X + distance * Mathf.Cos(direction),
                    at.Y + distance * Mathf.Sin(direction), 0f);
                eyeTargets[slot].enabled = true;
                DrawnEyeTargets++;
            }

            eyeMesh.Clear();
            eyeMesh.SetVertices(coneVertices);
            eyeMesh.SetColors(coneColors);
            eyeMesh.SetTriangles(coneTriangles, 0);
            eyeCones.gameObject.SetActive(true);
        }

        private void HideEyes()
        {
            DrawnEyeCones = 0;
            DrawnEyeTargets = 0;
            if (eyeCones == null)
                return;
            eyeCones.gameObject.SetActive(false);
            foreach (var target in eyeTargets)
                target.enabled = false;
        }

        private void EnsureEyeObjects()
        {
            if (eyeCones != null)
                return;
            var cones = new GameObject("Eye cones");
            cones.transform.SetParent(transform, false);
            eyeCones = cones.AddComponent<MeshFilter>();
            eyeMesh = new Mesh { name = "Eye cones" };
            eyeMesh.MarkDynamic();
            eyeCones.sharedMesh = eyeMesh;
            var renderer = cones.AddComponent<MeshRenderer>();
            renderer.sortingOrder = -1;
            if (spriteMaterial != null)
                renderer.sharedMaterial = spriteMaterial;
            for (var slot = 0; slot < eyeTargets.Length; slot++)
            {
                eyeTargets[slot] = CreateRenderer("Eye target", transform, circleSprite, TargetColor, 4);
                eyeTargets[slot].transform.localScale = Vector3.one * TargetDiameter;
                eyeTargets[slot].enabled = false;
            }
        }

        public Vector2 ScreenToArena(Vector2 screenPosition)
        {
            var world = targetCamera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, 0f));
            return transform.InverseTransformPoint(world);
        }

        // The remembered poses belong to the tick just before the one shown, in the same world.
        private bool HasPrevious(WorldState world) => previousWorld == world && previousTick == world.Tick - 1;

        private void RemoveStale()
        {
            staleCreatures.Clear();
            foreach (var pair in creatures)
                if (pair.Value.Frame != frame)
                    staleCreatures.Add(pair.Key);
            foreach (var id in staleCreatures)
            {
                Release(creatures[id]);
                creatures.Remove(id);
            }

            staleFood.Clear();
            foreach (var pair in food)
                if (pair.Value.Frame != frame)
                    staleFood.Add(pair.Key);
            foreach (var id in staleFood)
            {
                Release(food[id]);
                food.Remove(id);
            }
        }

        private void Release(CreatureVisual visual)
        {
            visual.Root.SetActive(false);
            creaturePool.Push(visual);
        }

        private void Release(FoodVisual visual)
        {
            visual.Renderer.gameObject.SetActive(false);
            foodPool.Push(visual);
        }

        private CreatureVisual CreateCreatureVisual()
        {
            var root = new GameObject("Creature");
            root.transform.SetParent(transform, false);
            var body = CreateRenderer("Body", root.transform, circleSprite, FedColor, 2);
            body.transform.localScale = Vector3.one * CreatureDiameter;
            var nose = CreateRenderer("Heading", root.transform, circleSprite, NoseColor, 3);
            nose.transform.localPosition = new Vector3(CreatureDiameter * 0.3f, 0f, 0f);
            nose.transform.localScale = Vector3.one * CreatureDiameter * 0.3f;
            var selection = CreateRenderer("Selection", root.transform, circleSprite, SelectionColor, 1);
            selection.transform.localScale = Vector3.one * CreatureDiameter * PointSelectionScale;
            selection.enabled = false;
            var reach = CreateRenderer("Reach", root.transform, circleSprite, ReachColor, 0);
            reach.enabled = false;
            var sensor = CreateRenderer("Sensor", root.transform, circleSprite, SensorColor, -1);
            sensor.enabled = false;
            return new CreatureVisual(root, body, selection, sensor, reach);
        }

        private FoodVisual CreateFoodVisual()
        {
            var renderer = CreateRenderer("Food", transform, circleSprite, FoodColor, 0);
            renderer.transform.localScale = Vector3.one * FoodDiameter;
            return new FoodVisual(renderer);
        }

        private SpriteRenderer CreateRenderer(string objectName, Transform parent, Sprite sprite, Color color, int order)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(parent, false);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            if (spriteMaterial != null)
                renderer.sharedMaterial = spriteMaterial;
            return renderer;
        }

        private readonly struct Pose
        {
            public Pose(float x, float y, float heading)
            {
                X = x;
                Y = y;
                Heading = heading;
            }

            public float X { get; }
            public float Y { get; }

            /// <summary>Radians, as in the simulation.</summary>
            public float Heading { get; }
        }

        private sealed class CreatureVisual
        {
            public CreatureVisual(GameObject root, SpriteRenderer body, SpriteRenderer selection, SpriteRenderer sensor,
                SpriteRenderer reach)
            {
                Root = root;
                Body = body;
                Selection = selection;
                Sensor = sensor;
                Reach = reach;
            }

            public GameObject Root { get; }
            public SpriteRenderer Body { get; }
            public SpriteRenderer Selection { get; }

            /// <summary>Disk of the sensor range, shown for the selected creature only.</summary>
            public SpriteRenderer Sensor { get; }

            /// <summary>Halo of the mouth's reach around a selected solid body.</summary>
            public SpriteRenderer Reach { get; }
            public int Frame { get; set; }
        }

        private sealed class FoodVisual
        {
            public FoodVisual(SpriteRenderer renderer)
            {
                Renderer = renderer;
            }

            public SpriteRenderer Renderer { get; }
            public int Frame { get; set; }
        }
    }
}
