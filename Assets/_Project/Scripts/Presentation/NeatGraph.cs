using System.Collections.Generic;
using Eden.Application;
using Eden.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eden.Presentation
{
    /// <summary>
    /// Draws a <see cref="NeatSnapshot"/>: the bias and the inputs in a labelled column on the left, the outputs on the
    /// right and hidden nodes in columns between them by depth, so every enabled connection that carries a signal points
    /// right. Nodes are colored by value and enabled connections by the signal they carry (weight × source value), like
    /// the MLP diagram; disabled connections are drawn thin and grey. Positions are laid out again only when the size or
    /// the network changes; nothing is generated while hidden or without a snapshot.
    /// </summary>
    public sealed class NeatGraph : VisualElement
    {
        private const float NodeRadius = 6f;
        private const float OutputRadius = 8f;
        private const float LabelWidth = 66f;
        private const float LabelGap = 10f;

        /// <summary>Most vertices a UI Toolkit mesh can index (16-bit indices).</summary>
        private const int MaxVertices = 65535;

        private static readonly Color32 Disabled = new Color32(120, 132, 138, 70);

        private readonly List<Label> labels = new List<Label>();
        private Vector2[] positions = new Vector2[0];
        private NeatSnapshot snapshot;
        private int laidOutVersion = -1;
        private Rect laidOutRect;

        public NeatGraph()
        {
            name = "neat-graph";
            pickingMode = PickingMode.Ignore;
            style.flexGrow = 1f;
            generateVisualContent += Generate;
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                Layout();
                MarkDirtyRepaint();
            });
        }

        /// <summary>Number of meshes generated.</summary>
        public int RepaintCount { get; private set; }

        /// <summary>Connections left out of the last drawing because the mesh would be too large (0 normally).</summary>
        public int SkippedConnections { get; private set; }

        /// <summary>Where a node slot of the snapshot is drawn (for tests); valid after the first drawing.</summary>
        public Vector2 PositionOf(int slot) => positions[slot];

        public void Show(NeatSnapshot data)
        {
            snapshot = data;
            Layout();
            MarkDirtyRepaint();
        }

        private void Generate(MeshGenerationContext context)
        {
            if (snapshot == null || !snapshot.HasData || laidOutVersion != snapshot.StructureVersion)
                return;

            RepaintCount++;
            var nodes = snapshot.NodeCount;
            var nodeVertices = nodes * NetworkGraph.DiscVertices;
            var drawable = Mathf.Max(0, (MaxVertices - nodeVertices) / NetworkGraph.LineVertices);
            var lines = Mathf.Min(snapshot.ConnectionCount, drawable);
            SkippedConnections = snapshot.ConnectionCount - lines;
            var mesh = context.Allocate(lines * NetworkGraph.LineVertices + nodeVertices,
                lines * NetworkGraph.LineIndices + nodes * NetworkGraph.DiscIndices);
            var next = 0;

            // Disabled connections first, so enabled ones are drawn over them.
            var drawn = 0;
            for (var pass = 0; pass < 2; pass++)
            {
                for (var c = 0; c < snapshot.ConnectionCount && drawn < lines; c++)
                {
                    var gene = snapshot.Connection(c);
                    if (gene.Enabled != (pass == 1))
                        continue;
                    var from = snapshot.FromSlot(c);
                    var color = gene.Enabled
                        ? NetworkGraph.SignalColor(gene.Weight * snapshot.Value(from))
                        : Disabled;
                    NetworkGraph.WriteLine(mesh, ref next, positions[from], positions[snapshot.ToSlot(c)], color);
                    drawn++;
                }
            }

            for (var s = 0; s < nodes; s++)
            {
                var radius = snapshot.Kind(s) == NeatNodeKind.Output ? OutputRadius : NodeRadius;
                NetworkGraph.WriteDisc(mesh, ref next, positions[s], radius, NetworkGraph.NeuronColor(snapshot.Value(s)));
            }
        }

        // Column x by depth, rows spread evenly within each column in slot order; labels for the contract's channels.
        // Runs outside repaints (labels are elements), when the snapshot's structure or the size changes.
        private void Layout()
        {
            var rect = contentRect;
            if (snapshot == null || !snapshot.HasData || rect.width <= 2f * (LabelWidth + LabelGap) || rect.height <= 0f)
                return;
            if (laidOutVersion == snapshot.StructureVersion && laidOutRect == rect)
                return;

            var nodes = snapshot.NodeCount;
            if (positions.Length != nodes)
                positions = new Vector2[nodes];
            var columns = snapshot.ColumnCount;
            var perColumn = new int[columns];
            for (var s = 0; s < nodes; s++)
                perColumn[snapshot.Column(s)]++;

            var left = LabelWidth + LabelGap;
            var right = rect.width - LabelWidth - LabelGap;
            var placed = new int[columns];
            for (var s = 0; s < nodes; s++)
            {
                var column = snapshot.Column(s);
                var x = columns == 1 ? left : left + (right - left) * column / (columns - 1);
                var y = rect.height * (placed[column] + 1) / (perColumn[column] + 1);
                placed[column]++;
                positions[s] = new Vector2(x, y);
            }

            PlaceLabels(right);
            laidOutVersion = snapshot.StructureVersion;
            laidOutRect = rect;
        }

        private static readonly string[] EyeLabels =
            { "presente", "comida vis.", "comida sen", "comida cos", "comida prox.", "barreira vis.", "barreira sen",
              "barreira cos", "barreira prox." };

        // Legacy inputs by name; with eyes, the body inputs and each eye slot's "present" input, so 41 inputs stay
        // readable in the panel (the others are unlabelled dots below their eye's label).
        private static string InputLabel(NeuralContract contract, int index)
        {
            if (contract == NeuralContract.Legacy)
                return NetworkGraph.InputNames[index];
            if (index < NeuralContract.EyesBodyInputs)
                return NetworkGraph.InputNames[index];
            var slot = (index - NeuralContract.EyesBodyInputs) / EyeChannels.Count;
            var channel = (index - NeuralContract.EyesBodyInputs) % EyeChannels.Count;
            return channel == (int)EyeChannel.Present ? $"olho {slot}: " + EyeLabels[channel] : null;
        }

        private void PlaceLabels(float right)
        {
            foreach (var label in labels)
                Remove(label);
            labels.Clear();
            for (var s = 0; s < snapshot.NodeCount; s++)
            {
                var kind = snapshot.Kind(s);
                string text;
                if (kind == NeatNodeKind.Bias)
                    text = "bias";
                else if (kind == NeatNodeKind.Input)
                    text = InputLabel(snapshot.Contract, snapshot.NodeId(s) - NeatNetwork.InputNodeId(0));
                else if (kind == NeatNodeKind.Output)
                    text = NetworkGraph.OutputNames[snapshot.NodeId(s) - NeatNetwork.OutputNodeId(snapshot.Contract, 0)];
                else
                    continue;
                if (text == null)
                    continue;

                var label = new Label(text) { pickingMode = PickingMode.Ignore };
                label.style.position = Position.Absolute;
                label.style.width = LabelWidth;
                label.style.height = 14f;
                label.style.fontSize = 11f;
                label.style.unityTextAlign = kind == NeatNodeKind.Output ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                label.style.color = (Color)new Color32(160, 190, 180, 255);
                label.style.left = kind == NeatNodeKind.Output ? right + LabelGap : 0f;
                label.style.top = positions[s].y - 7f;
                Add(label);
                labels.Add(label);
            }
        }
    }
}
