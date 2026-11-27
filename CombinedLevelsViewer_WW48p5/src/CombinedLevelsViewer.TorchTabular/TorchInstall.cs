using System.IO;

namespace CombinedLevelsViewer.TorchTabular;

/// <summary>Locates the web grid assets of the locally installed Torch extension.</summary>
internal static class TorchInstall
{
    public const string WebRootVariable = "TORCH_WEB_ROOT";

    public static string FindWebRoot()
    {
        var configured = Environment.GetEnvironmentVariable(WebRootVariable);
        if (!string.IsNullOrWhiteSpace(configured) && IsWebRoot(configured)) return Path.GetFullPath(configured);

        var torchRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Torch");
        if (Directory.Exists(torchRoot))
        {
            string? best = null;
            Version? bestVersion = null;
            foreach (var directory in Directory.GetDirectories(torchRoot))
            {
                if (!Version.TryParse(Path.GetFileName(directory), out var version)) continue;
                var webRoot = Path.Combine(directory, "Web");
                if (!IsWebRoot(webRoot)) continue;
                if (bestVersion == null || version > bestVersion)
                {
                    best = webRoot;
                    bestVersion = version;
                }
            }
            if (best != null) return best;
        }

        throw new DirectoryNotFoundException(
            "Could not find the Torch tabular web components under " + torchRoot + ". Install the Torch extension or set " + WebRootVariable + " to its Web folder.");
    }

    private static bool IsWebRoot(string path)
    {
        return File.Exists(Path.Combine(path, "Html", "BaseComponents", "Bootstrapper.js"))
            && File.Exists(Path.Combine(path, "Html", "externals", "scripts", "ag-grid-enterprise.min.js"));
    }
}
