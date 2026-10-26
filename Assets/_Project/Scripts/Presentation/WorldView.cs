using System.Collections.Generic;
using Eden.Domain;
using UnityEngine;

namespace Eden.Presentation
{
    /// <summary>
    /// Draws the world with pooled sprites: arena, food and creatures (body sized by its eat radius and colored by the
    /// share of its own capacity it holds, a nose dot showing heading, a ring and the sensor range on the selected one). Creatures are drawn between their pose before the last tick (see
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
            var referenceRadius = world.Config.EatRadius;
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
                var body = creature.Body;
                var size = body.EatRadius / referenceRadius;
                visual.Root.transform.localScale = new Vector3(size, size, 1f);
                visual.Body.color = Color.Lerp(HungryColor, FedColor, Mathf.Clamp01(creature.Energy / body.MaxEnergy));
                var isSelected = selected.HasValue && selected.Value == creature.Id;
                visual.Selection.enabled = isSelected;
                visual.Sensor.enabled = isSelected;
                if (isSelected)
                    visual.Sensor.transform.localScale = Vector3.one * (2f * body.SensorRange / size);
            }

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
            selection.transform.localScale = Vector3.one * CreatureDiameter * 1.5f;
            selection.enabled = false;
            var sensor = CreateRenderer("Sensor", root.transform, circleSprite, SensorColor, -1);
            sensor.enabled = false;
            return new CreatureVisual(root, body, selection, sensor);
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
            public CreatureVisual(GameObject root, SpriteRenderer body, SpriteRenderer selection, SpriteRenderer sensor)
            {
                Root = root;
                Body = body;
                Selection = selection;
                Sensor = sensor;
            }

            public GameObject Root { get; }
            public SpriteRenderer Body { get; }
            public SpriteRenderer Selection { get; }

            /// <summary>Disk of the sensor range, shown for the selected creature only.</summary>
            public SpriteRenderer Sensor { get; }
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
