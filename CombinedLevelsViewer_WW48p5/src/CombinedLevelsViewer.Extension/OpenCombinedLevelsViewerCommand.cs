using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.ProjectSystem.Query;

namespace CombinedLevelsViewer.Extension;

[VisualStudioContribution]
internal sealed class OpenCombinedLevelsViewerCommand : Command
{
    public OpenCombinedLevelsViewerCommand(VisualStudioExtensibility extensibility)
        : base(extensibility)
    {
    }

    public override CommandConfiguration CommandConfiguration => new("%OpenCombinedLevelsViewer.DisplayName%")
    {
        TooltipText = "%OpenCombinedLevelsViewer.Tooltip%",
        Placements = new[] { CommandPlacement.KnownPlacements.ExtensionsMenu },
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var solutions = await Extensibility.Workspaces().QuerySolutionAsync(
            query => query.With(solution => solution.Path),
            cancellationToken);
        var solution = solutions.FirstOrDefault();
        if (solution != null && !string.IsNullOrWhiteSpace(solution.Path))
        {
            var root = Path.GetDirectoryName(solution.Path);
            if (!string.IsNullOrWhiteSpace(root)) ViewerSession.ConfigureRoot(root);
        }

        await Extensibility.Shell().ShowToolWindowAsync<CombinedLevelsToolWindow>(activate: true, cancellationToken);
        await Extensibility.Shell().ShowToolWindowAsync<CombinedLevelsSourceToolWindow>(activate: true, cancellationToken);
    }
}
