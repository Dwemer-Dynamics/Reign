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

    [Theory]
    [InlineData("    JToken token = obj[key];")]
    [InlineData("    JToken token = obj?[\"supporterClanIds\"];")]
    [InlineData("    JToken token = JObject.Parse(json)[key];")]
    [InlineData("    return token == null ? fallback : token.Value<float>();")]
    [InlineData("    if (token==null||token.Type == JTokenType.Null)")]
    public void PreservesCSharpJsonLookupsAndNullComparisonsOnlyInSource(string line)
    {
        var redactor = new SensitiveDataRedactor();

        Assert.Equal(line, redactor.RedactSourceLine(line, isCSharp: true));
        Assert.Equal("[REDACTED: possible credential]", redactor.RedactSourceLine(line, isCSharp: false));
        Assert.Contains("[REDACTED]", redactor.RedactText(line), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("JToken token = \"fixture-opaque\";")]
    [InlineData("token = fixtureOpaque;")]
    [InlineData("if (token == \"fixture-opaque\") return;")]
    [InlineData("API_KEY=synthetic-fixture-value")]
    [InlineData("CLIENT_SECRET=synthetic-fixture-value")]
    [InlineData("""// {"openRouterApiKey":"fixture\"suffix"}""")]
    [InlineData("""token = "unterminated-synthetic-value""")]
    [InlineData("JToken token = obj[key]; // Bearer syntheticFixtureToken")]
    [InlineData("JToken token = obj[key]; // password=fixtureOpaque")]
    [InlineData("JToken token = obj[key]; // token = fixtureOpaque")]
    [InlineData("token = token == null")]
    [InlineData("access-token==null")]
    [InlineData("if (token==null?password=fixtureOpaque:fallback) return;")]
    public void MarksCredentialBearingSourceLinesWithoutReturningTheirText(string line)
    {
        Assert.Equal("[REDACTED: possible credential]",
            new SensitiveDataRedactor().RedactSourceLine(line, isCSharp: true));
    }

    [Fact]
    public void RecognizableCredentialStillTakesPrecedenceOverASourceExpression()
    {
        string credential = "AIza" + new string('x', 24);
        var line = $"JToken token = obj[\"{credential}\"];";

        Assert.Equal("[REDACTED: possible credential]",
            new SensitiveDataRedactor().RedactSourceLine(line, isCSharp: true));
    }

    [Fact]
    public void PreservesALongChainOfAdjacentNullComparisons()
    {
        var chain = string.Join("||", Enumerable.Repeat("(token==null)", 8_192));
        var line = "return " + chain + ";";

        Assert.Equal(line, new SensitiveDataRedactor().RedactSourceLine(line, isCSharp: true));
    }
}
