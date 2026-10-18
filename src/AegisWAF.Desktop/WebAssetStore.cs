using System.Reflection;
using System.IO;

namespace AegisWAF.Desktop;

/// <summary>Materializes the embedded admin console outside the single-file executable for ASP.NET static-file serving.</summary>
internal static class WebAssetStore
{
    private static readonly object Gate = new();
    private static string? _root;

    private static readonly (string Resource, string RelativePath)[] Assets =
    [
        ("AegisWAF.WebAssets.index.html", "index.html"),
        ("AegisWAF.WebAssets.assets.app.js", Path.Combine("assets", "app.js")),
        ("AegisWAF.WebAssets.assets.style.css", Path.Combine("assets", "style.css"))
    ];

    public static string EnsureExtracted()
    {
        lock (Gate)
        {
            if (_root is not null) return _root;
            var buildId = typeof(WebAssetStore).Assembly.ManifestModule.ModuleVersionId.ToString("N");
            var root = Path.Combine(Path.GetTempPath(), "AegisWAF", "web", buildId);
            foreach (var (resource, relativePath) in Assets)
            {
                var target = Path.Combine(root, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = typeof(WebAssetStore).Assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException($"Embedded web asset missing: {resource}");
                using var output = File.Create(target);
                input.CopyTo(output);
            }
            _root = root;
            return root;
        }
    }
}
