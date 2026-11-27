namespace CombinedLevelsViewer.Core.Tests;

/// <summary>
/// Locates test inputs without assuming which repository the code lives in, so the tests run unchanged
/// after the CombinedLevelsViewer folder is copied elsewhere.
/// </summary>
internal static class TestPaths
{
    public const string NoProductionFiles =
        "Shared/Common/LevelsSequences.lvl and BaseLevels.tcg were not found in any folder above the tests; this test only runs inside a test-program repository.";

    /// <summary>A file from tests/fixtures of the CombinedLevelsViewer folder.</summary>
    public static string Fixture(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", name);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("Could not find tests/fixtures/" + name + " above " + AppContext.BaseDirectory + ".");
    }

    /// <summary>The real levels and test-condition files of the repository the code sits in, when there is one.</summary>
    public static bool TryFindProductionFiles(out string levelsPath, out string testConditionsPath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            levelsPath = Path.Combine(directory.FullName, "Shared", "Common", "LevelsSequences.lvl");
            testConditionsPath = Path.Combine(directory.FullName, "Shared", "Common", "BaseLevels.tcg");
            if (File.Exists(levelsPath) && File.Exists(testConditionsPath)) return true;
        }

        levelsPath = string.Empty;
        testConditionsPath = string.Empty;
        return false;
    }
}
