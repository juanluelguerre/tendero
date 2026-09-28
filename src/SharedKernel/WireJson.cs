using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// How a closed set travels (ADR 0043): as its C# member name, and never as a
/// number. One definition, because the API and the MCP server serialise the same
/// records with two different option sets, and a rule written twice is two rules.
/// </summary>
public static class WireJson
{
    public static JsonConverter Enums() => new NamesOnlyEnumConverter();

    /// <summary>
    /// The framework's string converter, which the OpenAPI generator recognises
    /// and declares as the enum's names, with one refusal added: the text read
    /// must BE a member's name. Left alone it ORs <c>"Day, Week"</c> into
    /// <c>Week</c> — a real member, so nothing downstream could tell — which is
    /// the same gap <see cref="WireEnum{TEnum}"/> closes in a query string.
    /// </summary>
    private sealed class NamesOnlyEnumConverter : JsonConverterFactory
    {
        private readonly JsonStringEnumConverter names = new(allowIntegerValues: false);

        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter?)Activator.CreateInstance(
                typeof(NameOnly<>).MakeGenericType(typeToConvert),
                names.CreateConverter(typeToConvert, options));
    }

    private sealed class NameOnly<TEnum>(JsonConverter<TEnum> inner) : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (text is not null && text.Contains(','))
                throw new JsonException($"'{text}' is not one {typeof(TEnum).Name}: a closed set is one member at a time.");

            return inner.Read(ref reader, typeToConvert, options);
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            inner.Write(writer, value, options);

        public override TEnum ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            inner.ReadAsPropertyName(ref reader, typeToConvert, options);

        public override void WriteAsPropertyName(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            inner.WriteAsPropertyName(writer, value, options);
    }
}
