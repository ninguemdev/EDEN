using UnityEngine;
using UnityEngine.UIElements;

namespace Eden.Presentation
{
    /// <summary>Thin binding to the lab UXML: run summary on the left, creature inspector on the right.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LabHud : MonoBehaviour
    {
        private Label runStats;
        private Label inspector;
        private VisualElement inspectorPanel;
        private bool inspectorVisible;

        public string RunText => Bind() ? runStats.text : string.Empty;
        public string InspectorText => Bind() && inspectorVisible ? inspector.text : string.Empty;

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

        private bool Bind()
        {
            if (runStats != null)
                return true;
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
                return false;
            runStats = root.Q<Label>("run-stats");
            inspector = root.Q<Label>("inspector");
            inspectorPanel = root.Q<VisualElement>("inspector-panel");
            return runStats != null && inspector != null && inspectorPanel != null;
        }
    }
}
