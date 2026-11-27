using CombinedLevelsViewer.Core;

namespace CombinedLevelsViewer.Extension;

internal static class ViewerSession
{
    private static readonly object Gate = new object();
    private static PairedDocumentService? service;

    public static event EventHandler<SourceNavigationEventArgs>? SourceNavigationRequested;
    public static event EventHandler<string>? LevelSelectionRequested;

    public static PairedDocumentService GetOrCreateService()
    {
        lock (Gate)
        {
            if (service != null) return service;
            var root = FindRoot();
            service = new PairedDocumentService(
                Path.Combine(root, "Shared", "Common", "LevelsSequences.lvl"),
                Path.Combine(root, "Shared", "Common", "BaseLevels.tcg"));
            return service;
        }
    }

    public static void ConfigureRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A repository root is required.", nameof(root));
        lock (Gate)
        {
            service?.Dispose();
            service = new PairedDocumentService(
                Path.Combine(Path.GetFullPath(root), "Shared", "Common", "LevelsSequences.lvl"),
                Path.Combine(Path.GetFullPath(root), "Shared", "Common", "BaseLevels.tcg"));
        }
    }

    public static void NavigateToSource(PairedDocumentKind kind, TextSpan span, string levelName)
    {
        SourceNavigationRequested?.Invoke(null, new SourceNavigationEventArgs(kind, span));
        LevelSelectionRequested?.Invoke(null, levelName);
    }

    public static void SelectLevelAt(PairedDocumentKind kind, int position)
    {
        if (kind != PairedDocumentKind.Levels || service == null) return;
        foreach (var level in service.Snapshot.LastValidModel.LevelSets)
        {
            if (position >= level.LevelSet.Span.Start && position <= level.LevelSet.Span.End)
            {
                LevelSelectionRequested?.Invoke(null, level.LevelSet.Name);
                return;
            }
        }
    }

    private static string FindRoot()
    {
        var configured = Environment.GetEnvironmentVariable("COMBINED_LEVELS_ROOT");
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
            "Could not locate Shared/Common/LevelsSequences.lvl and BaseLevels.tcg. Set COMBINED_LEVELS_ROOT to the repository root.");
    }

    private static bool HasPair(string root)
    {
        return File.Exists(Path.Combine(root, "Shared", "Common", "LevelsSequences.lvl"))
            && File.Exists(Path.Combine(root, "Shared", "Common", "BaseLevels.tcg"));
    }
}

internal sealed class SourceNavigationEventArgs : EventArgs
{
    public SourceNavigationEventArgs(PairedDocumentKind kind, TextSpan span)
    {
        Kind = kind;
        Span = span;
    }

    public PairedDocumentKind Kind { get; }
    public TextSpan Span { get; }
}
