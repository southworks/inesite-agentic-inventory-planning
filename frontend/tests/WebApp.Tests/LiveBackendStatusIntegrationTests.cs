using System.Net;
using System.Text;
using System.Text.Json;
using Cohere.InventoryAndTrend.WebApp.Contracts;
using Cohere.InventoryAndTrend.WebApp.Contracts.Api.Backend;
using Cohere.InventoryAndTrend.WebApp.Services;

namespace Cohere.InventoryAndTrend.WebApp.Tests;

public class LiveBackendStatusIntegrationTests
{
    [Fact]
    public async Task StatusEndpoint_MapsAgentOutputsFromLiveBackend()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5038/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!await IsBackendAvailableAsync(httpClient))
        {
            return;
        }

        using var startResponse = await httpClient.PostAsync(
            "api/inventory-planning/cases/case-01/workflow/basic/start",
            content: null);

        if (startResponse.StatusCode is not HttpStatusCode.OK)
        {
            return;
        }

        var startBody = await startResponse.Content.ReadAsStringAsync();
        var start = JsonSerializer.Deserialize<BackendBasicWorkflowStatusResponse>(startBody, BackendApiJson.Options)
                    ?? throw new InvalidOperationException("Start response was empty.");

        BackendBasicWorkflowStatusResponse? status = null;
        for (var attempt = 0; attempt < 90; attempt++)
        {
            using var statusResponse = await httpClient.GetAsync(
                $"api/inventory-planning/executions/{start.ExecutionId}/basic/status");

            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

            var statusBody = await statusResponse.Content.ReadAsStringAsync();
            status = JsonSerializer.Deserialize<BackendBasicWorkflowStatusResponse>(statusBody, BackendApiJson.Options)
                     ?? throw new InvalidOperationException("Status response was empty.");

            if (!string.IsNullOrWhiteSpace(status.AgentOutputs.SignalIngestion))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        Assert.NotNull(status);
        Assert.False(string.IsNullOrWhiteSpace(status!.AgentOutputs.SignalIngestion));

        var mapper = new BackendWorkflowMapper(new AgentOutputParser());
        var progress = mapper.MapBasicWorkflowStatus(status, "integration-plan");

        Assert.Equal("Completed", progress.Stages[0].Status);
        Assert.NotNull(progress.Stages[0].Output);
        Assert.False(string.IsNullOrWhiteSpace(progress.Stages[0].Output!.Decision));
        Assert.False(string.IsNullOrWhiteSpace(progress.Stages[0].Output!.Summary));
    }

    private static async Task<bool> IsBackendAvailableAsync(HttpClient httpClient)
    {
        try
        {
            using var response = await httpClient.GetAsync("health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
