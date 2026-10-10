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

    [Fact]
    public void RedactsNamedProvidersAndNestedProfileCredentialsWithoutChangingSafeFields()
    {
        using var document = JsonDocument.Parse("""
            {"entries":[{"data":{"nanoGptApiKey":"nano-fixture","openRouterApiKey":"router-fixture",
            "QDRANT_API_KEY":"vector-fixture","profiles":[{"clientSecret":"client-fixture","custom-api-key":"custom-fixture"}],
            "maxTokens":1200,"memoryCheckerModel":"fast-model","sourceKey":"accepted-source","enabled":true}}]}
            """);
        var redactor = new SensitiveDataRedactor();
        foreach (var result in new[] { redactor.Redact(document.RootElement), redactor.RedactDiagnosticPayload(document.RootElement) })
        {
            var text = result.GetRawText();
            foreach (var secret in new[] { "nano-fixture", "router-fixture", "vector-fixture", "client-fixture", "custom-fixture" })
                Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
            var data = result.GetProperty("entries")[0].GetProperty("data");
            Assert.Equal(1200, data.GetProperty("maxTokens").GetInt32());
            Assert.Equal("fast-model", data.GetProperty("memoryCheckerModel").GetString());
            Assert.Equal("accepted-source", data.GetProperty("sourceKey").GetString());
            Assert.True(data.GetProperty("enabled").GetBoolean());
        }
        Assert.Equal("nano-fixture", document.RootElement.GetProperty("entries")[0].GetProperty("data").GetProperty("nanoGptApiKey").GetString());
    }

    [Theory]
    [InlineData("nanoGptApiKey=nano-fixture safe", "nano-fixture")]
    [InlineData("{\"openRouterApiKey\":\"router-fixture\",\"maxTokens\":12}", "router-fixture")]
    [InlineData("database_password='multi word fixture' safe", "multi word fixture")]
    public void RedactsProviderCredentialsInText(string input, string secret)
    {
        var result = new SensitiveDataRedactor().RedactText(input);
        Assert.DoesNotContain(secret, result, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactsRecognizableSyntheticCredentialValue()
    {
        string credential = "sk-" + new string('x', 24);
        var result = new SensitiveDataRedactor().RedactText("message " + credential + " safe");
        Assert.DoesNotContain(credential, result, StringComparison.Ordinal);
        Assert.Contains("safe", result, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result, StringComparison.Ordinal);
    }
}
