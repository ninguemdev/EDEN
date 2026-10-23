using System.Collections.Generic;
using Eden.Domain;
using UnityEngine;

namespace Eden.Presentation
{
    /// <summary>
    /// Draws the current world state with pooled sprites: arena, food and creatures (body colored by energy,
    /// a nose dot showing heading, a ring on the selected one). Read-only: it never changes simulation state
    /// and never draws from simulation random streams. Arena units map 1:1 to Unity world units.
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
        private SpriteRenderer arena;
        private int frame;

        public int VisibleCreatures => creatures.Count;
        public int VisibleFood => food.Count;

        /// <summary>Resets visuals for a new world and fits the camera to its arena.</summary>
        public void Bind(WorldState world)
        {
            foreach (var visual in creatures.Values)
                Release(visual);
            creatures.Clear();
            foreach (var visual in food.Values)
                Release(visual);
            food.Clear();

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

        public void Render(WorldState world, CreatureId? selected)
        {
            frame++;
            var maxEnergy = world.Config.MaxEnergy;
            foreach (var creature in world.Creatures)
            {
                if (!creatures.TryGetValue(creature.Id, out var visual))
                {
                    visual = creaturePool.Count > 0 ? creaturePool.Pop() : CreateCreatureVisual();
                    visual.Root.SetActive(true);
                    creatures.Add(creature.Id, visual);
                }

                visual.Frame = frame;
                visual.Root.transform.localPosition = new Vector3(creature.Position.X, creature.Position.Y, 0f);
                visual.Root.transform.localRotation = Quaternion.Euler(0f, 0f, creature.Heading * Mathf.Rad2Deg);
                visual.Body.color = Color.Lerp(HungryColor, FedColor, Mathf.Clamp01(creature.Energy / maxEnergy));
                visual.Selection.enabled = selected.HasValue && selected.Value == creature.Id;
            }

            foreach (var item in world.Food)
            {
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
            return new CreatureVisual(root, body, selection);
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

        private sealed class CreatureVisual
        {
            public CreatureVisual(GameObject root, SpriteRenderer body, SpriteRenderer selection)
            {
                Root = root;
                Body = body;
                Selection = selection;
            }

            public GameObject Root { get; }
            public SpriteRenderer Body { get; }
            public SpriteRenderer Selection { get; }
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
