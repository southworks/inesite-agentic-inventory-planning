using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cohere.InventoryAndTrend.WebApp.Contracts.Api.Backend;

/// <summary>
/// Backend agent outputs may arrive as JSON strings or embedded objects depending on serializer settings.
/// </summary>
public sealed class BackendAgentOutputJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.StartObject or JsonTokenType.StartArray:
            {
                using var document = JsonDocument.ParseValue(ref reader);
                return document.RootElement.GetRawText();
            }
            default:
                throw new JsonException(
                    $"Unexpected token {reader.TokenType} when parsing an agent output field.");
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }
}
