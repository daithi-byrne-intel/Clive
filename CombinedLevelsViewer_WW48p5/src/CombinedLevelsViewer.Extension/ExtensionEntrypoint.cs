using Microsoft.VisualStudio.Extensibility;

namespace CombinedLevelsViewer.Extension;

[VisualStudioContribution]
internal sealed class ExtensionEntrypoint : Microsoft.VisualStudio.Extensibility.Extension
{
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new ExtensionMetadata(
            id: "Intel.CombinedLevelsViewer",
            version: ExtensionAssemblyVersion,
            publisherName: "Intel",
            displayName: "Combined Levels Viewer",
            description: "A synchronized tabular and source viewer for Torch level and test-condition files."),
    };
}
