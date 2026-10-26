using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Eden.Persistence
{
    internal enum JsonKind
    {
        Null,
        Boolean,
        Number,
        String,
        Array,
        Object
    }

    /// <summary>
    /// Minimal JSON tree (RFC 8259) for checkpoints. Numbers keep their exact text and are converted only when read
    /// with the expected type, always with the invariant culture; floats are written with the shortest text that reads
    /// back to the same bits, so values round-trip exactly. Readers report the path of a bad value.
    /// </summary>
    internal sealed class JsonValue
    {
        private readonly List<JsonValue> items;
        private readonly List<KeyValuePair<string, JsonValue>> members;
        private JsonValue parent;
        private string key;
        private int index;

        private JsonValue(JsonKind kind, string text = null)
        {
            Kind = kind;
            Text = text;
            if (kind == JsonKind.Array)
                items = new List<JsonValue>();
            else if (kind == JsonKind.Object)
                members = new List<KeyValuePair<string, JsonValue>>();
        }

        public JsonKind Kind { get; }

        /// <summary>String value, or the literal text of a number or boolean.</summary>
        public string Text { get; }

        public int Count => Kind == JsonKind.Array ? items.Count : Kind == JsonKind.Object ? members.Count : 0;

        /// <summary>Location in the document, e.g. <c>state.evolution.population[3]</c>.</summary>
        public string Path
        {
            get
            {
                if (parent == null)
                    return "$";
                var prefix = parent.parent == null ? string.Empty : parent.Path;
                return key != null ? (prefix.Length == 0 ? key : prefix + "." + key) : prefix + "[" + index + "]";
            }
        }

        public static JsonValue Null() => new JsonValue(JsonKind.Null);
        public static JsonValue NewArray() => new JsonValue(JsonKind.Array);
        public static JsonValue NewObject() => new JsonValue(JsonKind.Object);
        public static JsonValue Of(bool value) => new JsonValue(JsonKind.Boolean, value ? "true" : "false");
        public static JsonValue Of(string value) =>
            new JsonValue(JsonKind.String, value ?? throw new ArgumentNullException(nameof(value)));
        public static JsonValue Of(int value) => Number(value.ToString(CultureInfo.InvariantCulture));
        public static JsonValue Of(long value) => Number(value.ToString(CultureInfo.InvariantCulture));
        public static JsonValue Of(ulong value) => Number(value.ToString(CultureInfo.InvariantCulture));

        /// <summary>A finite float; NaN and infinities have no JSON number form (see <see cref="OfAnyFloat"/>).</summary>
        public static JsonValue Of(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException($"Non-finite value {value} cannot be saved as a number.", nameof(value));
            return Number(ShortestRoundTrip(value));
        }

        /// <summary>A float that may be NaN or infinite (e.g. an invalid fitness), written as a string in that case.</summary>
        public static JsonValue OfAnyFloat(float value) =>
            float.IsNaN(value) ? Of("NaN")
            : float.IsPositiveInfinity(value) ? Of("Infinity")
            : float.IsNegativeInfinity(value) ? Of("-Infinity")
            : Of(value);

        public JsonValue Add(JsonValue value)
        {
            if (Kind != JsonKind.Array)
                throw new InvalidOperationException("Only arrays take items.");
            Adopt(value, null, items.Count);
            items.Add(value);
            return this;
        }

        public JsonValue Add(string name, JsonValue value)
        {
            if (Kind != JsonKind.Object)
                throw new InvalidOperationException("Only objects take members.");
            if (Find(name) != null)
                throw new ArgumentException($"Duplicate key \"{name}\".", nameof(name));
            Adopt(value, name, members.Count);
            members.Add(new KeyValuePair<string, JsonValue>(name, value));
            return this;
        }

        public JsonValue this[int position]
        {
            get
            {
                RequireKind(JsonKind.Array);
                return items[position];
            }
        }

        /// <summary>Required member of an object.</summary>
        public JsonValue this[string name]
        {
            get
            {
                RequireKind(JsonKind.Object);
                return Find(name) ?? throw new CheckpointException($"{Path}: missing \"{name}\".");
            }
        }

        /// <summary>Optional member of an object, or null when it is absent.</summary>
        public JsonValue TryGet(string name)
        {
            RequireKind(JsonKind.Object);
            return Find(name);
        }

        public bool IsNull => Kind == JsonKind.Null;

        /// <summary>Array of at most <paramref name="max"/> items, checked before the caller allocates anything for them.</summary>
        public JsonValue ArrayOf(int max)
        {
            RequireKind(JsonKind.Array);
            if (items.Count > max)
                throw new CheckpointException($"{Path}: {items.Count} items exceed the limit of {max}.");
            return this;
        }

        public string String()
        {
            RequireKind(JsonKind.String);
            return Text;
        }

        public bool Boolean()
        {
            RequireKind(JsonKind.Boolean);
            return Text == "true";
        }

        public int Int(int min = int.MinValue, int max = int.MaxValue)
        {
            var value = Long(min, max);
            return (int)value;
        }

        public long Long(long min = long.MinValue, long max = long.MaxValue)
        {
            RequireKind(JsonKind.Number);
            if (!long.TryParse(Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ||
                value < min || value > max)
                throw new CheckpointException($"{Path}: {Text} is not an integer in [{min}, {max}].");
            return value;
        }

        public ulong ULong()
        {
            RequireKind(JsonKind.Number);
            if (!ulong.TryParse(Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new CheckpointException($"{Path}: {Text} is not an unsigned 64-bit integer.");
            return value;
        }

        /// <summary>A finite float.</summary>
        public float Float()
        {
            RequireKind(JsonKind.Number);
            var value = ParseFloat(Text);
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new CheckpointException($"{Path}: {Text} is not a finite float.");
            return value;
        }

        /// <summary>A float written by <see cref="OfAnyFloat"/>.</summary>
        public float AnyFloat()
        {
            if (Kind != JsonKind.String)
                return Float();
            switch (Text)
            {
                case "NaN": return float.NaN;
                case "Infinity": return float.PositiveInfinity;
                case "-Infinity": return float.NegativeInfinity;
                default: throw new CheckpointException($"{Path}: \"{Text}\" is not a float.");
            }
        }

        /// <summary>Exactly the text <see cref="JsonText.Write"/> produces without indentation; used for hashes.</summary>
        public string ToCompactString()
        {
            var builder = new StringBuilder();
            JsonText.Write(this, builder, -1, 0);
            return builder.ToString();
        }

        internal IEnumerable<KeyValuePair<string, JsonValue>> Members => members;
        internal IEnumerable<JsonValue> Items => items;

        internal static JsonValue Number(string text) => new JsonValue(JsonKind.Number, text);
        internal static JsonValue Literal(JsonKind kind, string text) => new JsonValue(kind, text);

        private static string ShortestRoundTrip(float value)
        {
            if (value == 0f)
                return BitConverter.SingleToInt32Bits(value) < 0 ? "-0" : "0";
            for (var digits = 6; digits <= 9; digits++)
            {
                var text = value.ToString("G" + digits, CultureInfo.InvariantCulture);
                if (BitConverter.SingleToInt32Bits(ParseFloat(text)) == BitConverter.SingleToInt32Bits(value))
                    return text;
            }

            throw new InvalidOperationException($"No round-trip text for {value:R}.");
        }

        private static float ParseFloat(string text)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return float.NaN;
            // Some runtimes drop the sign of zero when parsing; the text keeps it.
            return value == 0f && text.StartsWith("-", StringComparison.Ordinal) ? -0f : value;
        }

        private void Adopt(JsonValue child, string name, int position)
        {
            if (child == null)
                throw new ArgumentNullException(nameof(child));
            if (child.parent != null)
                throw new ArgumentException("A value can only belong to one parent.", nameof(child));
            child.parent = this;
            child.key = name;
            child.index = position;
        }

        private JsonValue Find(string name)
        {
            foreach (var member in members)
            {
                if (member.Key == name)
                    return member.Value;
            }

            return null;
        }

        private void RequireKind(JsonKind expected)
        {
            if (Kind != expected)
                throw new CheckpointException($"{Path}: expected {expected.ToString().ToLowerInvariant()}, " +
                                              $"found {Kind.ToString().ToLowerInvariant()}.");
        }
    }

    /// <summary>Strict JSON reading and writing for <see cref="JsonValue"/>.</summary>
    internal static class JsonText
    {
        public const int MaxDepth = 32;

        /// <summary>Text with two-space indentation for objects; arrays of plain values stay on one line.</summary>
        public static string Write(JsonValue value)
        {
            var builder = new StringBuilder();
            Write(value, builder, 0, 0);
            builder.Append('\n');
            return builder.ToString();
        }

        /// <summary>Parses one JSON value; anything else in the text (or nesting deeper than <see cref="MaxDepth"/>) is an error.</summary>
        public static JsonValue Parse(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            var parser = new Parser(text);
            var value = parser.ParseValue(0);
            parser.SkipWhitespace();
            if (!parser.AtEnd)
                throw parser.Error("unexpected text after the document");
            return value;
        }

        // indent < 0 writes compact text.
        internal static void Write(JsonValue value, StringBuilder builder, int indent, int depth)
        {
            switch (value.Kind)
            {
                case JsonKind.Null:
                    builder.Append("null");
                    break;
                case JsonKind.Boolean:
                case JsonKind.Number:
                    builder.Append(value.Text);
                    break;
                case JsonKind.String:
                    WriteString(value.Text, builder);
                    break;
                case JsonKind.Array:
                    WriteArray(value, builder, indent, depth);
                    break;
                default:
                    WriteObject(value, builder, indent, depth);
                    break;
            }
        }

        private static void WriteArray(JsonValue value, StringBuilder builder, int indent, int depth)
        {
            var nested = false;
            foreach (var item in value.Items)
                nested |= item.Kind == JsonKind.Array || item.Kind == JsonKind.Object;
            var multiline = indent >= 0 && nested;

            builder.Append('[');
            var first = true;
            foreach (var item in value.Items)
            {
                if (!first)
                    builder.Append(',');
                if (multiline)
                    NewLine(builder, depth + 1);
                Write(item, builder, indent, depth + 1);
                first = false;
            }

            if (multiline && !first)
                NewLine(builder, depth);
            builder.Append(']');
        }

        private static void WriteObject(JsonValue value, StringBuilder builder, int indent, int depth)
        {
            builder.Append('{');
            var first = true;
            foreach (var member in value.Members)
            {
                if (!first)
                    builder.Append(',');
                if (indent >= 0)
                    NewLine(builder, depth + 1);
                WriteString(member.Key, builder);
                builder.Append(indent >= 0 ? ": " : ":");
                Write(member.Value, builder, indent, depth + 1);
                first = false;
            }

            if (indent >= 0 && !first)
                NewLine(builder, depth);
            builder.Append('}');
        }

        private static void NewLine(StringBuilder builder, int depth)
        {
            builder.Append('\n');
            builder.Append(' ', depth * 2);
        }

        private static void WriteString(string text, StringBuilder builder)
        {
            builder.Append('"');
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(c);
                        break;
                }
            }

            builder.Append('"');
        }

        private sealed class Parser
        {
            private readonly string text;
            private int position;

            public Parser(string text)
            {
                this.text = text;
            }

            public bool AtEnd => position >= text.Length;

            public JsonValue ParseValue(int depth)
            {
                if (depth > MaxDepth)
                    throw Error($"nesting deeper than {MaxDepth}");
                SkipWhitespace();
                if (AtEnd)
                    throw Error("unexpected end of text");
                var c = text[position];
                switch (c)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return JsonValue.Of(ParseString());
                    case 't': return Keyword("true", JsonKind.Boolean);
                    case 'f': return Keyword("false", JsonKind.Boolean);
                    case 'n': return Keyword("null", JsonKind.Null);
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return ParseNumber();
                        throw Error($"unexpected character '{c}'");
                }
            }

            public void SkipWhitespace()
            {
                while (!AtEnd && (text[position] == ' ' || text[position] == '\n' || text[position] == '\r' ||
                                  text[position] == '\t'))
                    position++;
            }

            public CheckpointException Error(string message)
            {
                var line = 1;
                var column = 1;
                for (var i = 0; i < position && i < text.Length; i++)
                {
                    if (text[i] == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else
                    {
                        column++;
                    }
                }

                return new CheckpointException($"Invalid JSON at line {line}, column {column}: {message}.");
            }

            private JsonValue ParseObject(int depth)
            {
                var value = JsonValue.NewObject();
                position++;
                SkipWhitespace();
                if (Peek('}'))
                    return value;
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || text[position] != '"')
                        throw Error("expected a key");
                    var name = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    var member = ParseValue(depth + 1);
                    try
                    {
                        value.Add(name, member);
                    }
                    catch (ArgumentException)
                    {
                        throw Error($"duplicate key \"{name}\"");
                    }

                    SkipWhitespace();
                    if (Peek('}'))
                        return value;
                    Expect(',');
                }
            }

            private JsonValue ParseArray(int depth)
            {
                var value = JsonValue.NewArray();
                position++;
                SkipWhitespace();
                if (Peek(']'))
                    return value;
                while (true)
                {
                    value.Add(ParseValue(depth + 1));
                    SkipWhitespace();
                    if (Peek(']'))
                        return value;
                    Expect(',');
                }
            }

            private string ParseString()
            {
                position++;
                var builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                        throw Error("unterminated string");
                    var c = text[position++];
                    if (c == '"')
                        return builder.ToString();
                    if (c < 0x20)
                        throw Error("control character in string");
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (AtEnd)
                        throw Error("unterminated escape");
                    var escape = text[position++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (position + 4 > text.Length || !int.TryParse(text.Substring(position, 4),
                                    NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                                throw Error("invalid \\u escape");
                            builder.Append((char)code);
                            position += 4;
                            break;
                        default:
                            throw Error($"invalid escape \\{escape}");
                    }
                }
            }

            // -?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?
            private JsonValue ParseNumber()
            {
                var start = position;
                if (Peek('-') && AtEnd)
                    throw Error("incomplete number");
                if (Peek('0'))
                {
                    if (!AtEnd && IsDigit(text[position]))
                        throw Error("leading zero in number");
                }
                else if (!Digits())
                {
                    throw Error("expected a digit");
                }

                if (Peek('.') && !Digits())
                    throw Error("expected a digit after the decimal point");
                if (Peek('e') || Peek('E'))
                {
                    if (!Peek('+'))
                        Peek('-');
                    if (!Digits())
                        throw Error("expected an exponent");
                }

                return JsonValue.Number(text.Substring(start, position - start));
            }

            private bool Digits()
            {
                var start = position;
                while (!AtEnd && IsDigit(text[position]))
                    position++;
                return position > start;
            }

            private static bool IsDigit(char c) => c >= '0' && c <= '9';

            private JsonValue Keyword(string word, JsonKind kind)
            {
                if (string.CompareOrdinal(text, position, word, 0, word.Length) != 0)
                    throw Error($"expected {word}");
                position += word.Length;
                return JsonValue.Literal(kind, word);
            }

            private bool Peek(char c)
            {
                if (AtEnd || text[position] != c)
                    return false;
                position++;
                return true;
            }

            private void Expect(char c)
            {
                if (!Peek(c))
                    throw Error($"expected '{c}'");
            }
        }
    }
}
