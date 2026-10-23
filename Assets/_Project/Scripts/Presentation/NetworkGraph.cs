using Eden.Application;
using Eden.Brains;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eden.Presentation
{
    /// <summary>
    /// Draws a <see cref="NeuralSnapshot"/> of the 12 → 16 → 3 network: neurons colored by activation and
    /// connections colored by the signal they carry (weight × source activation). Neuron positions are computed
    /// once per panel size; a repaint only rewrites vertex colors into a mesh of fixed size. Nothing is generated
    /// while the element is hidden (display: none) or has no snapshot.
    /// </summary>
    public sealed class NetworkGraph : VisualElement
    {
        private const int CircleSegments = 12;
        private const float NeuronRadius = 6f;
        private const float OutputRadius = 8f;
        private const float LineWidth = 1.5f;
        private const float LabelWidth = 66f;
        private const float LabelGap = 10f;

        private const int Connections = NeuralLayout.Inputs * NeuralLayout.Hidden + NeuralLayout.Hidden * NeuralLayout.Outputs;
        private const int Neurons = NeuralLayout.Inputs + NeuralLayout.Hidden + NeuralLayout.Outputs;
        private const int VertexCount = Connections * 4 + Neurons * (CircleSegments + 1);
        private const int IndexCount = Connections * 6 + Neurons * CircleSegments * 3;

        private static readonly string[] InputNames =
        {
            "energia", "idade", "vel. frente", "vel. esq.", "comida sen", "comida cos", "comida prox.",
            "comida vis.", "parede F", "parede D", "parede T", "parede E"
        };

        private static readonly string[] OutputNames = { "propulsão", "giro", "lateral" };

        private static readonly Color32 Positive = new Color32(94, 214, 168, 255);
        private static readonly Color32 Negative = new Color32(232, 96, 76, 255);
        private static readonly Color32 Idle = new Color32(58, 72, 78, 255);

        private readonly Vector2[] inputPositions = new Vector2[NeuralLayout.Inputs];
        private readonly Vector2[] hiddenPositions = new Vector2[NeuralLayout.Hidden];
        private readonly Vector2[] outputPositions = new Vector2[NeuralLayout.Outputs];
        private readonly Vector2[] circle = new Vector2[CircleSegments];
        private readonly Label[] inputLabels = new Label[NeuralLayout.Inputs];
        private readonly Label[] outputLabels = new Label[NeuralLayout.Outputs];
        private NeuralSnapshot snapshot;
        private bool layoutReady;

        public NetworkGraph()
        {
            name = "network-graph";
            pickingMode = PickingMode.Ignore;
            style.flexGrow = 1f;
            for (var k = 0; k < CircleSegments; k++)
            {
                var angle = k * Mathf.PI * 2f / CircleSegments;
                circle[k] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            for (var i = 0; i < inputLabels.Length; i++)
                inputLabels[i] = AddLabel(InputNames[i], TextAnchor.MiddleRight);
            for (var o = 0; o < outputLabels.Length; o++)
                outputLabels[o] = AddLabel(OutputNames[o], TextAnchor.MiddleLeft);

            generateVisualContent += Generate;
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        /// <summary>Number of meshes generated; stays constant while nothing is selected.</summary>
        public int RepaintCount { get; private set; }

        public void Show(NeuralSnapshot data)
        {
            snapshot = data;
            MarkDirtyRepaint();
        }

        private Label AddLabel(string text, TextAnchor anchor)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.width = LabelWidth;
            label.style.height = 14f;
            label.style.fontSize = 11f;
            label.style.unityTextAlign = anchor;
            label.style.color = (Color)new Color32(160, 190, 180, 255);
            label.style.paddingLeft = 0f;
            label.style.paddingRight = 0f;
            label.style.marginLeft = 0f;
            label.style.marginRight = 0f;
            Add(label);
            return label;
        }

        private void Layout()
        {
            var rect = contentRect;
            if (rect.width <= 2f * (LabelWidth + LabelGap) || rect.height <= 0f)
                return;

            var inputX = LabelWidth + LabelGap;
            var outputX = rect.width - LabelWidth - LabelGap;
            PlaceColumn(inputPositions, inputX, rect.height);
            PlaceColumn(hiddenPositions, (inputX + outputX) / 2f, rect.height);
            PlaceColumn(outputPositions, outputX, rect.height);

            for (var i = 0; i < inputLabels.Length; i++)
            {
                inputLabels[i].style.left = 0f;
                inputLabels[i].style.top = inputPositions[i].y - 7f;
            }

            for (var o = 0; o < outputLabels.Length; o++)
            {
                outputLabels[o].style.left = outputX + LabelGap;
                outputLabels[o].style.top = outputPositions[o].y - 7f;
            }

            layoutReady = true;
            MarkDirtyRepaint();
        }

        private static void PlaceColumn(Vector2[] positions, float x, float height)
        {
            var step = height / (positions.Length + 1);
            var offset = positions.Length == NeuralLayout.Outputs ? height / 4f : 0f;
            var span = positions.Length == NeuralLayout.Outputs ? (height - 2f * offset) / (positions.Length + 1) : step;
            for (var i = 0; i < positions.Length; i++)
                positions[i] = new Vector2(x, offset + span * (i + 1));
        }

        private void Generate(MeshGenerationContext context)
        {
            if (!layoutReady || snapshot == null || !snapshot.HasData)
                return;

            RepaintCount++;
            var mesh = context.Allocate(VertexCount, IndexCount);
            var next = 0;

            for (var h = 0; h < NeuralLayout.Hidden; h++)
            for (var i = 0; i < NeuralLayout.Inputs; i++)
            {
                var signal = snapshot.Weight(NeuralLayout.HiddenWeight(h, i)) * snapshot.Input(i);
                WriteLine(mesh, ref next, inputPositions[i], hiddenPositions[h], SignalColor(signal));
            }

            for (var o = 0; o < NeuralLayout.Outputs; o++)
            for (var h = 0; h < NeuralLayout.Hidden; h++)
            {
                var signal = snapshot.Weight(NeuralLayout.OutputWeight(o, h)) * snapshot.Hidden(h);
                WriteLine(mesh, ref next, hiddenPositions[h], outputPositions[o], SignalColor(signal));
            }

            for (var i = 0; i < NeuralLayout.Inputs; i++)
                WriteDisc(mesh, ref next, inputPositions[i], NeuronRadius, NeuronColor(snapshot.Input(i)));
            for (var h = 0; h < NeuralLayout.Hidden; h++)
                WriteDisc(mesh, ref next, hiddenPositions[h], NeuronRadius, NeuronColor(snapshot.Hidden(h)));
            for (var o = 0; o < NeuralLayout.Outputs; o++)
                WriteDisc(mesh, ref next, outputPositions[o], OutputRadius, NeuronColor(snapshot.Output(o)));
        }

        /// <summary>Neutral grey at 0, shading to green for positive and red for negative activations.</summary>
        public static Color32 NeuronColor(float activation)
        {
            var amount = Mathf.Clamp01(Mathf.Abs(activation));
            return Color32.Lerp(Idle, activation >= 0f ? Positive : Negative, amount);
        }

        /// <summary>Faint for weak signals, bright green/red for strong positive/negative ones.</summary>
        public static Color32 SignalColor(float signal)
        {
            var color = signal >= 0f ? Positive : Negative;
            color.a = (byte)(20 + 215 * Mathf.Clamp01(Mathf.Abs(signal)));
            return color;
        }

        private static void WriteLine(MeshWriteData mesh, ref int next, Vector2 from, Vector2 to, Color32 color)
        {
            var direction = (to - from).normalized;
            var normal = new Vector2(-direction.y, direction.x) * (LineWidth / 2f);
            var start = next;
            mesh.SetNextVertex(MakeVertex(from + normal, color));
            mesh.SetNextVertex(MakeVertex(to + normal, color));
            mesh.SetNextVertex(MakeVertex(to - normal, color));
            mesh.SetNextVertex(MakeVertex(from - normal, color));
            next += 4;
            Triangle(mesh, start, start + 1, start + 2, from + normal, to + normal, to - normal);
            Triangle(mesh, start, start + 2, start + 3, from + normal, to - normal, from - normal);
        }

        private void WriteDisc(MeshWriteData mesh, ref int next, Vector2 center, float radius, Color32 color)
        {
            var start = next;
            mesh.SetNextVertex(MakeVertex(center, color));
            for (var k = 0; k < CircleSegments; k++)
                mesh.SetNextVertex(MakeVertex(center + circle[k] * radius, color));
            next += CircleSegments + 1;
            for (var k = 0; k < CircleSegments; k++)
            {
                var a = (k + 1) % CircleSegments;
                Triangle(mesh, start, start + 1 + k, start + 1 + a,
                    center, center + circle[k] * radius, center + circle[a] * radius);
            }
        }

        // UI Toolkit draws triangles wound clockwise on screen (y grows downwards); fix the order if needed.
        private static void Triangle(MeshWriteData mesh, int i0, int i1, int i2, Vector2 p0, Vector2 p1, Vector2 p2)
        {
            var cross = (p1.x - p0.x) * (p2.y - p0.y) - (p1.y - p0.y) * (p2.x - p0.x);
            mesh.SetNextIndex((ushort)i0);
            mesh.SetNextIndex((ushort)(cross >= 0f ? i1 : i2));
            mesh.SetNextIndex((ushort)(cross >= 0f ? i2 : i1));
        }

        private static Vertex MakeVertex(Vector2 position, Color32 color) =>
            new Vertex { position = new Vector3(position.x, position.y, Vertex.nearZ), tint = color };
    }
}
