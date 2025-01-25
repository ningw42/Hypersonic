using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypersonic.Client.Models.JsonConverters;

/// <summary>
/// A string converter operates in a forgiving manner that converts the token below into string
/// 1) JSON boolean
/// 2) JSON number
/// </summary>
public class ForgivingStringConverter : JsonConverter<string>
{
    /// <inheritdoc/>
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.False => "false",
            JsonTokenType.True => "true",
            JsonTokenType.Number => Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan),
            _ => reader.GetString(),
        };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        // We don't handle serialization
        writer.WriteStringValue(value);
    }
}
