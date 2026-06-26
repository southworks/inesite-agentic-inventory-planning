using System.Net.Http.Headers;
using System.Text.Json;

namespace CohereInventoryAndTrend.Mcp.Adapters;

internal static class FoundryHttpExtensions
{
    public static ByteArrayContent CreateJsonContent<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    public static async Task EnsureFoundrySuccessAsync(
        this HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = string.IsNullOrWhiteSpace(body)
            ? string.Empty
            : $" Response body: {Truncate(body, 1024)}";

        throw new HttpRequestException(
            $"Foundry {operation} request failed with {(int)response.StatusCode} ({response.ReasonPhrase}).{detail}");
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
