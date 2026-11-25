using System.IO;

namespace CombinedLevelsViewer.TorchTabular;

internal static class RepositoryLocator
{
    public const string RootVariable = "COMBINED_LEVELS_ROOT";

    public static string LevelsPath(string root) => Path.Combine(root, "Shared", "Common", "LevelsSequences.lvl");

    public static string TestConditionsPath(string root) => Path.Combine(root, "Shared", "Common", "BaseLevels.tcg");

    public static string UserVarsPath(string root) => Path.Combine(root, "Shared", "Common", "UservarDefinitions.usrv");

    public static string FindRoot(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            if (File.Exists(explicitRoot)) return FindRootForFile(explicitRoot);
            if (HasPair(explicitRoot)) return Path.GetFullPath(explicitRoot);
            throw new DirectoryNotFoundException("'" + explicitRoot + "' does not contain Shared/Common/LevelsSequences.lvl and BaseLevels.tcg.");
        }

        var configured = Environment.GetEnvironmentVariable(RootVariable);
        if (!string.IsNullOrWhiteSpace(configured) && HasPair(configured)) return Path.GetFullPath(configured);

        foreach (var startingPath in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(startingPath);
            while (directory != null)
            {
                if (HasPair(directory.FullName)) return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate Shared/Common/LevelsSequences.lvl and BaseLevels.tcg. Pass the repository root as the first argument or set " + RootVariable + ".");
    }

    /// <summary>
    /// Supports "Open With" on the files themselves: the argument is LevelsSequences.lvl or
    /// BaseLevels.tcg, and the repository root is the folder above it that holds the pair.
    /// </summary>
    private static string FindRootForFile(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = new DirectoryInfo(Path.GetDirectoryName(fullPath) ?? ".");
        while (directory != null)
        {
            if (HasPair(directory.FullName))
            {
                var root = directory.FullName;
                if (IsSameFile(fullPath, LevelsPath(root)) || IsSameFile(fullPath, TestConditionsPath(root))) return root;
                break;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "'" + Path.GetFileName(fullPath) + "' is not one of the files this viewer shows. Open Shared/Common/LevelsSequences.lvl or Shared/Common/BaseLevels.tcg.");
    }

    private static bool IsSameFile(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool HasPair(string root) => File.Exists(LevelsPath(root)) && File.Exists(TestConditionsPath(root));
}
