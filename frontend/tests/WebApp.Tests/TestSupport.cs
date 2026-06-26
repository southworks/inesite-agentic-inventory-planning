using Cohere.InventoryAndTrend.WebApp.Configuration;
using Cohere.InventoryAndTrend.WebApp.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Cohere.InventoryAndTrend.WebApp.Tests;

public static class TestSupport
{
    public static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    public static string DatasetSeedRoot => Path.Combine(RepoRoot, "dataset-seed");

    public static string WebAppContentRoot => Path.Combine(RepoRoot, "frontend", "src", "WebApp");

    public static BackendCaseCatalogService CreateCaseCatalogService()
    {
        var options = Options.Create(new DatasetSeedOptions { RootPath = DatasetSeedRoot });
        return new BackendCaseCatalogService(options, new TestWebHostEnvironment(RepoRoot));
    }

    public static BackendCaseCatalogService CreateCaseCatalogServiceFromAppSettingsRelativePath()
    {
        var options = Options.Create(new DatasetSeedOptions { RootPath = "../../../dataset-seed" });
        return new BackendCaseCatalogService(options, new TestWebHostEnvironment(WebAppContentRoot));
    }

    public static string ReadBackendFixture(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "frontend", "tests", "WebApp.Tests", "Fixtures", "Backend", fileName));

    public sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string contentRoot) => ContentRootPath = contentRoot;

        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
