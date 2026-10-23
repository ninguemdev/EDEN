using Eden.Application;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eden.Presentation
{
    /// <summary>
    /// Thin binding to the lab UXML: run summary on the left, creature inspector on the right and, for a
    /// selected neural creature, the network diagram below the inspector.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LabHud : MonoBehaviour
    {
        private Label runStats;
        private Label inspector;
        private VisualElement inspectorPanel;
        private VisualElement networkPanel;
        private bool inspectorVisible;

        public string RunText => Bind() ? runStats.text : string.Empty;
        public string InspectorText => Bind() && inspectorVisible ? inspector.text : string.Empty;
        public bool NetworkVisible { get; private set; }
        public NetworkGraph Graph { get; private set; }

        public void ShowRun(string text)
        {
            if (Bind())
                runStats.text = text;
        }

        /// <summary>Shows the inspector with <paramref name="text"/>, or hides it when the text is null.</summary>
        public void ShowInspector(string text)
        {
            if (!Bind())
                return;
            inspectorVisible = text != null;
            inspectorPanel.style.display = inspectorVisible ? DisplayStyle.Flex : DisplayStyle.None;
            inspector.text = text ?? string.Empty;
        }

        /// <summary>Shows the diagram for a snapshot, or hides it (no drawing at all) when null.</summary>
        public void ShowNetwork(NeuralSnapshot snapshot)
        {
            if (!Bind())
                return;
            var visible = snapshot != null;
            if (visible != NetworkVisible)
            {
                networkPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                NetworkVisible = visible;
            }

            if (visible)
                Graph.Show(snapshot);
        }

        private bool Bind()
        {
            if (runStats != null)
                return true;
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
                return false;
            var stats = root.Q<Label>("run-stats");
            var inspectorLabel = root.Q<Label>("inspector");
            var inspectorElement = root.Q<VisualElement>("inspector-panel");
            var networkElement = root.Q<VisualElement>("network-panel");
            var host = root.Q<VisualElement>("network-graph-host");
            if (stats == null || inspectorLabel == null || inspectorElement == null || networkElement == null || host == null)
                return false;

            inspector = inspectorLabel;
            inspectorPanel = inspectorElement;
            networkPanel = networkElement;
            Graph = new NetworkGraph();
            host.Add(Graph);
            runStats = stats;
            return true;
        }
    }
}
