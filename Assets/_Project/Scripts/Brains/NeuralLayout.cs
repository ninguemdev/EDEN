namespace Eden.Brains
{
    /// <summary>
    /// Topology and flat parameter order of the fixed 12 → 16 → 3 network. Inference, mutation and saves all use
    /// this single order:
    /// <list type="number">
    /// <item>hidden weights, hidden-major: index <c>h * Inputs + i</c> (offset 0, 192 values);</item>
    /// <item>hidden biases (offset 192, 16 values);</item>
    /// <item>output weights, output-major: index <c>o * Hidden + h</c> (offset 208, 48 values);</item>
    /// <item>output biases (offset 256, 3 values).</item>
    /// </list>
    /// </summary>
    public static class NeuralLayout
    {
        public const int Inputs = 12;
        public const int Hidden = 16;
        public const int Outputs = 3;

        public const int HiddenWeightsOffset = 0;
        public const int HiddenBiasOffset = HiddenWeightsOffset + Inputs * Hidden;
        public const int OutputWeightsOffset = HiddenBiasOffset + Hidden;
        public const int OutputBiasOffset = OutputWeightsOffset + Hidden * Outputs;
        public const int ParameterCount = OutputBiasOffset + Outputs;

        public static int HiddenWeight(int hidden, int input) => HiddenWeightsOffset + hidden * Inputs + input;
        public static int HiddenBias(int hidden) => HiddenBiasOffset + hidden;
        public static int OutputWeight(int output, int hidden) => OutputWeightsOffset + output * Hidden + hidden;
        public static int OutputBias(int output) => OutputBiasOffset + output;
    }
}
