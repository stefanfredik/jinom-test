using System.Text.Json;
using System.Text.Json.Serialization;

namespace FoTestingApp.Services.Core;

/// <summary>
/// Reads an enum from its name in any case ("PASS", "pass", "Partial") and falls back to a given
/// value for a name it does not know, instead of throwing — one unexpected status from the server
/// must not empty a whole list. Writes the name in upper case, as the API stores it.
/// </summary>
public sealed class TolerantEnumConverter<TEnum>(TEnum fallback) : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<TEnum>(reader.GetString(), ignoreCase: true, out var value))
        {
            return value;
        }

        return fallback;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString().ToUpperInvariant());
}
