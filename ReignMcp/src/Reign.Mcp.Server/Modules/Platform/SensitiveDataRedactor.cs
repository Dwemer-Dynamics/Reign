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

    [GeneratedRegex("""(?i)\b([A-Za-z0-9_-]*(?:api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret|password)|token|secret)(["']?\s*[:=]\s*)(?:"[^"]*"|'[^']*'|[^\s,;}"']+)""")]
    private static partial Regex LabeledSecretPattern();

    [GeneratedRegex(@"\b(?:sk-[A-Za-z0-9_-]{12,}|AIza[A-Za-z0-9_-]{20,})\b")]
    private static partial Regex KnownCredentialPattern();

    private static bool IsSensitiveKey(string name)
    {
        if (SensitiveKeys.Contains(name)) return true;
        var normalized = name.Replace("_", "").Replace("-", "");
        return new[] { "apiKey", "accessToken", "refreshToken", "clientSecret", "password" }
            .Any(suffix => normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

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
        result = LabeledSecretPattern().Replace(result, "$1$2\"[REDACTED]\"");
        return KnownCredentialPattern().Replace(result, "[REDACTED]");
    }

    // This single endpoint returns immutable, credential-redacted text from the
    // explicit capture store. Rewriting fiction's "secret" or a partial token
    // across page boundaries would corrupt its content hashes and pagination.
    public JsonElement RedactDiagnosticPayload(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return Redact(input);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in input.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (property.Name == "content" && property.Value.ValueKind == JsonValueKind.String)
                    property.Value.WriteTo(writer);
                else if (IsSensitiveKey(property.Name)) writer.WriteStringValue("[REDACTED]");
                else WriteElement(writer, property.Value, property.Name);
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
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
                    if (IsSensitiveKey(property.Name))
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
