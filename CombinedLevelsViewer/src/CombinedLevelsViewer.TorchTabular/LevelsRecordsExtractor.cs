using System.Globalization;
using CombinedLevelsViewer.Core;

namespace CombinedLevelsViewer.TorchTabular;

/// <summary>Cell payload understood by Torch's tabular grid (see Torch Web\Html\BaseComponents).</summary>
internal sealed record GridCell(string Data, int CellOptions, string? CustomToolTipMessage = null);

/// <summary>Row payload understood by Torch's tabular grid.</summary>
internal sealed record GridRecord(string RowId, Dictionary<string, GridCell> CellsData);

/// <summary>
/// Converts a projected level table into Torch tabular grid records, the same role
/// FlowItemRecordExtractor and JsonTabularRecordsExtractor play for Torch's own designers.
/// </summary>
internal static class LevelsRecordsExtractor
{
    /// <summary>Must match LevelsConsts.RowNameColumn in the web components.</summary>
    public const string RowNameColumn = "Resource / Break";

    /// <summary>Suffix of the read-only column that shows the uservar a clamp resolves to.</summary>
    public const string UserVarColumnSuffix = " (uservar)";

    // Same bit values as Torch's TabularCellOptions.
    private const int None = 0;
    private const int Editable = 1;
    private const int Invalid = 4;

    /// <summary>
    /// Builds the grid records for a level set as written. Next to every clamp column a read-only column shows
    /// the uservar and value that the clamp resolves to; <paramref name="unresolvedClamps"/> counts cells
    /// without an exact match.
    /// </summary>
    public static List<GridRecord> Extract(LevelTable table, UserVarsDocument userVars, out int unresolvedClamps)
    {
        unresolvedClamps = 0;
        var resolutions = new Dictionary<string, ClampResolution?>(StringComparer.Ordinal);
        var records = new List<GridRecord>(table.Rows.Count);
        for (var index = 0; index < table.Rows.Count; index++)
        {
            var row = table.Rows[index];
            var cells = new Dictionary<string, GridCell>(StringComparer.Ordinal)
            {
                [RowNameColumn] = new GridCell(row.Name, None),
            };
            foreach (var cell in row.Cells)
            {
                cells[cell.Column] = new GridCell(cell.Value, cell.EditableSpan.HasValue ? Editable : None);
                if (!ClampResolver.IsClampProperty(cell.Column)) continue;

                ClampResolution? resolution = null;
                if (cell.Value.Length > 0 && !resolutions.TryGetValue(cell.Value, out resolution))
                {
                    resolution = ClampResolver.Resolve(cell.Value, table.Groups, userVars);
                    resolutions.Add(cell.Value, resolution);
                }

                if (resolution != null && resolution.HasProblem) unresolvedClamps++;
                cells[cell.Column + UserVarColumnSuffix] = resolution == null
                    ? new GridCell(string.Empty, None)
                    : new GridCell(resolution.Summary, resolution.HasProblem ? Invalid : None, resolution.Details);
            }
            records.Add(new GridRecord(ToRowId(index), cells));
        }
        return records;
    }

    /// <summary>
    /// Builds grid records for one test condition: every parameter is replaced by its value for
    /// <paramref name="selector"/>. A cell is editable when its value is a literal held by the level or by one
    /// entry of the specification set; the tooltip shows the substitutions and what an edit would change.
    /// A cell whose parameter has no exact definition keeps its expression and is flagged.
    /// </summary>
    public static List<GridRecord> ExtractResolved(LevelTable table, LinkedTestConditionGroup group, string selector, ParameterResolver resolver, string testConditionsText, out int unresolved)
    {
        unresolved = 0;
        var cache = new Dictionary<string, (ResolvedParameter Resolved, ParameterEditTarget Target)>(StringComparer.Ordinal);
        var records = new List<GridRecord>(table.Rows.Count);
        for (var index = 0; index < table.Rows.Count; index++)
        {
            var row = table.Rows[index];
            var cells = new Dictionary<string, GridCell>(StringComparer.Ordinal)
            {
                [RowNameColumn] = new GridCell(row.Name, None),
            };
            foreach (var cell in row.Cells)
            {
                if (cell.Value.Length == 0 || !ParameterResolver.IsParameterProperty(cell.Column))
                {
                    cells[cell.Column] = new GridCell(cell.Value, cell.EditableSpan.HasValue ? Editable : None);
                    continue;
                }

                if (!cache.TryGetValue(cell.Value, out var entry))
                {
                    entry = (resolver.Resolve(cell.Value, group, selector), resolver.FindEditTarget(cell.Value, group, selector, testConditionsText));
                    cache.Add(cell.Value, entry);
                }

                var resolved = entry.Resolved;
                var target = entry.Target;
                var editable = target.Editable && (target.Kind == PairedDocumentKind.TestConditions || cell.EditableSpan.HasValue);
                if (resolved.Problem != null) unresolved++;

                string? tooltip = null;
                if (resolved.Changed || resolved.Problem != null)
                {
                    tooltip = resolved.Trace + "\n" + (editable
                        ? "An edit changes " + target.Owner + (target.AppliesToAllSelectors ? " (one value shared by all selectors)." : " for selector " + selector + ".")
                        : "Not editable here: " + target.Reason);
                }

                cells[cell.Column] = new GridCell(resolved.Text, (resolved.Problem != null ? Invalid : None) | (editable ? Editable : None), tooltip);
            }
            records.Add(new GridRecord(ToRowId(index), cells));
        }
        return records;
    }

    public static string ToRowId(int index) => index.ToString(CultureInfo.InvariantCulture);

    public static bool TryParseRowId(string? rowId, out int index)
        => int.TryParse(rowId, NumberStyles.None, CultureInfo.InvariantCulture, out index);
}
