using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Reign.Mcp.Server;

public sealed partial class SensitiveDataRedactor
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "apiKey", "api_key", "apikey", "password", "secret",
        "token", "accessToken", "access_token", "refreshToken", "refresh_token",
        "clientSecret", "client_secret"
    };

    [GeneratedRegex(@"(?i)\b(Bearer\s+)[A-Za-z0-9._~+/=-]{8,}")]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"(?i)\b(api[_-]?key|token|password|secret)\s*[:=]\s*([^\s,;""']{6,})")]
    private static partial Regex LabeledSecretPattern();

    public JsonElement Redact(JsonElement input)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteElement(writer, input, null);
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    public string RedactText(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var result = BearerPattern().Replace(input, "$1[REDACTED]");
        return LabeledSecretPattern().Replace(result, "$1=[REDACTED]");
    }

    private void WriteElement(Utf8JsonWriter writer, JsonElement element, string? propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (SensitiveKeys.Contains(property.Name))
                    {
                        writer.WriteStringValue("[REDACTED]");
                    }
                    else
                    {
                        WriteElement(writer, property.Value, property.Name);
                    }
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item, propertyName);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(RedactText(element.GetString() ?? string.Empty));
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}

