using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class SensitiveDataRedactorTests
{
    [Fact]
    public void RecursivelyRedactsSecretKeysAndBearerTokens()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "apiKey": "super-secret-value",
              "nested": {
                "Authorization": "Bearer abcdefghijklmnop",
                "message": "token=abcdefghijklmnop"
              },
              "safe": "campaign_123"
            }
            """);
        var result = new SensitiveDataRedactor().Redact(document.RootElement);
        var text = result.GetRawText();

        Assert.DoesNotContain("super-secret-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghijklmnop", text, StringComparison.Ordinal);
        Assert.Contains("campaign_123", text, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", text, StringComparison.Ordinal);
    }
}

