using System.Text.Json;

namespace GrokInventoryAndTrend.WebApp.Services;

public static class BackendApiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
