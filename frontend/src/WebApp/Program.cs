using Cohere.InventoryAndTrend.WebApp.Components;
using Cohere.InventoryAndTrend.WebApp.Configuration;
using Cohere.InventoryAndTrend.WebApp.Services;
using Cohere.InventoryAndTrend.WebApp.State;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<DatasetSeedOptions>(
    builder.Configuration.GetSection(DatasetSeedOptions.SectionName));
builder.Services.Configure<WorkflowPollingOptions>(
    builder.Configuration.GetSection(WorkflowPollingOptions.SectionName));
builder.Services.Configure<PlanningApiOptions>(
    builder.Configuration.GetSection(PlanningApiOptions.SectionName));

builder.Services.AddSingleton<DatasetSeedCatalogService>();
builder.Services.AddSingleton<AgentOutputParser>();
builder.Services.AddSingleton<BackendWorkflowMapper>();
builder.Services.AddSingleton<LocalPlanningSimulator>();

builder.Services.AddScoped<PlanSessionStore>();
builder.Services.AddScoped<ScenarioPickerState>();
builder.Services.AddScoped<RecentPlansListState>();
builder.Services.AddScoped<PlanWorkspaceState>();
builder.Services.AddScoped<PlanWorkspaceSectionState>();

var planningApiOptions = builder.Configuration.GetSection(PlanningApiOptions.SectionName).Get<PlanningApiOptions>()
                         ?? new PlanningApiOptions();

if (planningApiOptions.IsRemote)
{
    builder.Services.AddHttpClient<IPlanningApiClient, PlanningApiClient>(client =>
        client.BaseAddress = new Uri(planningApiOptions.BaseUrl));
}
else
{
    builder.Services.AddScoped<IPlanningApiClient, LocalPlanningApiClient>();
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
