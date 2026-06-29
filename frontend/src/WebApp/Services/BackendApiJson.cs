using System.Text.Json;

namespace Grok.InventoryAndTrend.WebApp.Services;

public static class BackendApiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
