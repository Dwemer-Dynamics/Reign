using System.Net;
using System.Text;
using System.Text.Json;

namespace Reign.Mcp.Server;

public sealed class ReignApiClient(
    HttpClient httpClient,
    ReignMcpOptions options,
    SensitiveDataRedactor redactor)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ReignMcpOptions _options = options;
    private readonly SensitiveDataRedactor _redactor = redactor;

    public Task<ApiEnvelope> GetAsync(
        string path,
        IReadOnlyDictionary<string, string?>? query = null,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Get, path, query, null, cancellationToken);
    }

    public Task<ApiEnvelope> PostAsync(
        string path,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Post, path, null, body ?? new { }, cancellationToken);
    }

    private async Task<ApiEnvelope> SendAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        object? body,
        CancellationToken cancellationToken)
    {
        ValidatePath(path);
        var endpoint = BuildEndpoint(path, query);
        try
        {
            using var request = new HttpRequestMessage(method, endpoint);
            request.Headers.UserAgent.ParseAdd("ReignMcp/0.1");
            if (body is not null)
            {
                request.Content = new StringContent(
                    JsonSerializer.Serialize(body, body.GetType()),
                    Encoding.UTF8,
                    "application/json");
            }

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            var bytes = await ReadBoundedAsync(
                response.Content,
                _options.MaxApiResponseBytes,
                cancellationToken).ConfigureAwait(false);
            var text = Encoding.UTF8.GetString(bytes);

            JsonElement? data = null;
            string? error = null;
            try
            {
                using var document = JsonDocument.Parse(text);
                data = _redactor.Redact(document.RootElement);
                if (data.Value.ValueKind == JsonValueKind.Object
                    && data.Value.TryGetProperty("ok", out var okProperty)
                    && okProperty.ValueKind == JsonValueKind.False
                    && data.Value.TryGetProperty("error", out var errorProperty))
                {
                    error = errorProperty.GetString();
                }
            }
            catch (JsonException)
            {
                error = "Reign returned a non-JSON response.";
            }

            return new ApiEnvelope
            {
                Ok = response.IsSuccessStatusCode && error is null,
                Endpoint = endpoint.PathAndQuery,
                RetrievedUtc = DateTime.UtcNow.ToString("O"),
                StatusCode = (int)response.StatusCode,
                Data = data,
                Error = error ?? (response.IsSuccessStatusCode
                    ? null
                    : $"Reign returned HTTP {(int)response.StatusCode} ({response.StatusCode}).")
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(endpoint, null, "The Reign API request timed out.");
        }
        catch (HttpRequestException exception)
        {
            return Failure(endpoint, exception.StatusCode, _redactor.RedactText(exception.Message));
        }
        catch (InvalidDataException exception)
        {
            return Failure(endpoint, null, exception.Message);
        }
    }

    private ApiEnvelope Failure(Uri endpoint, HttpStatusCode? statusCode, string error)
    {
        return new ApiEnvelope
        {
            Ok = false,
            Endpoint = endpoint.PathAndQuery,
            RetrievedUtc = DateTime.UtcNow.ToString("O"),
            StatusCode = statusCode is null ? null : (int)statusCode,
            Error = error
        };
    }

    private Uri BuildEndpoint(string path, IReadOnlyDictionary<string, string?>? query)
    {
        var builder = new UriBuilder(new Uri(_options.ServerBaseUri, path));
        if (query is { Count: > 0 })
        {
            builder.Query = string.Join("&", query
                .Where(pair => pair.Value is not null)
                .Select(pair =>
                    Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value!)));
        }
        return builder.Uri;
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !path.StartsWith("/", StringComparison.Ordinal)
            || path.Contains("..", StringComparison.Ordinal)
            || path.Contains('?', StringComparison.Ordinal)
            || path.Contains('#', StringComparison.Ordinal)
            || Uri.TryCreate(path, UriKind.Absolute, out _))
        {
            throw new ArgumentException("The Reign API path must be a fixed relative path.", nameof(path));
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException(
                $"Reign returned more than the configured {maximumBytes} byte response limit.");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            if (destination.Length + read > maximumBytes)
            {
                throw new InvalidDataException(
                    $"Reign returned more than the configured {maximumBytes} byte response limit.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return destination.ToArray();
    }
}
