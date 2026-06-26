using System.Text.Json;

namespace Cohere.InventoryAndTrend.WebApp.Services;

public static class BackendApiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
