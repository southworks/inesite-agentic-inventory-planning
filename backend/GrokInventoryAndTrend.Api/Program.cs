using GrokInventoryAndTrend.Api.Options;
using GrokInventoryAndTrend.Api.Services;
using GrokInventoryAndTrend.Api.Workflow;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.Configure<AzureFoundryOptions>(options =>
{
    builder.Configuration.GetSection(AzureFoundryOptions.SectionName).Bind(options);

    string? endpoint = builder.Configuration["AZURE_FOUNDRY_PROJECT_ENDPOINT"]
        ?? Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_ENDPOINT");

    if (!string.IsNullOrWhiteSpace(endpoint))
    {
        options.ProjectEndpoint = endpoint;
    }
});

builder.Services.Configure<DatasetOptions>(options =>
{
    builder.Configuration.GetSection(DatasetOptions.SectionName).Bind(options);

    string? rootPath = builder.Configuration["Dataset__RootPath"]
        ?? Environment.GetEnvironmentVariable("Dataset__RootPath");

    if (!string.IsNullOrWhiteSpace(rootPath))
    {
        options.RootPath = rootPath;
    }
});

builder.Services.Configure<DocumentExtractionOptions>(
    builder.Configuration.GetSection(DocumentExtractionOptions.SectionName));

StartupConfigurationValidator.Validate(builder.Configuration);

builder.Services.AddSingleton<FoundryAgentProvider>();
builder.Services.AddSingleton<InventoryPlanningBasicWorkflowFactory>();
builder.Services.AddSingleton<LocalDocumentStorageService>();
builder.Services.AddSingleton<CaseCatalogService>();
builder.Services.AddSingleton<DocumentTextExtractionService>();
builder.Services.AddSingleton<InMemoryBasicWorkflowStore>();
builder.Services.AddSingleton<InventoryPlanningWorkflowService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();
