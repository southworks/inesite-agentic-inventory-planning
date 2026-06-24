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

    public static string DatasetSeedRoot => Path.Combine(RepoRoot, "frontend", "dataset-seed");

    public static DatasetSeedCatalogService CreateCatalogService()
    {
        var options = Options.Create(new DatasetSeedOptions
        {
            RootPath = DatasetSeedRoot
        });

        return new DatasetSeedCatalogService(options, new TestWebHostEnvironment(RepoRoot));
    }

    public static string WebAppContentRoot => Path.Combine(RepoRoot, "frontend", "src", "WebApp");

    public static DatasetSeedCatalogService CreateCatalogServiceFromAppSettingsRelativePath()
    {
        var options = Options.Create(new DatasetSeedOptions { RootPath = "../../dataset-seed" });
        return new DatasetSeedCatalogService(options, new TestWebHostEnvironment(WebAppContentRoot));
    }

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
