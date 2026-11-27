using System;
using System.Collections.Generic;

namespace CombinedLevelsViewer.Core;

public sealed class LevelTable
{
    public LevelTable(string name, IReadOnlyList<string> columns, IReadOnlyList<LevelTableRow> rows, IReadOnlyList<LinkedTestConditionGroup> groups)
    {
        Name = name;
        Columns = columns;
        Rows = rows;
        Groups = groups;
    }

    public string Name { get; }
    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyList<LevelTableRow> Rows { get; }
    public IReadOnlyList<LinkedTestConditionGroup> Groups { get; }
}

public sealed class LevelTableRow
{
    public LevelTableRow(string name, string kind, IReadOnlyList<LevelTableCell> cells, TextSpan sourceSpan)
    {
        Name = name;
        Kind = kind;
        Cells = cells;
        SourceSpan = sourceSpan;
    }

    public string Name { get; }
    public string Kind { get; }
    public IReadOnlyList<LevelTableCell> Cells { get; }
    public TextSpan SourceSpan { get; }
}

public sealed class LevelTableCell
{
    public LevelTableCell(string column, string value, TextSpan? editableSpan)
    {
        Column = column;
        Value = value;
        EditableSpan = editableSpan;
    }

    public string Column { get; }
    public string Value { get; }
    public TextSpan? EditableSpan { get; }
}

public static class LevelTableProjector
{
    public static LevelTable Project(LinkedLevelSet linkedLevel)
    {
        if (linkedLevel == null) throw new ArgumentNullException(nameof(linkedLevel));
        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in linkedLevel.LevelSet.Entries)
        {
            if (entry is SequenceBreakSyntax && seen.Add("Delay")) columns.Add("Delay");
            if (entry is ResourceEntrySyntax resource)
            {
                foreach (var property in resource.Properties)
                {
                    if (seen.Add(property.Name)) columns.Add(property.Name);
                }
            }
        }

        var rows = new List<LevelTableRow>();
        var breakIndex = 0;
        foreach (var entry in linkedLevel.LevelSet.Entries)
        {
            var cells = new List<LevelTableCell>();
            if (entry is SequenceBreakSyntax sequenceBreak)
            {
                foreach (var column in columns)
                {
                    cells.Add(new LevelTableCell(column, column == "Delay" ? sequenceBreak.Expression : string.Empty, column == "Delay" ? sequenceBreak.ExpressionSpan : (TextSpan?)null));
                }
                rows.Add(new LevelTableRow("SequenceBreak [" + breakIndex++ + "]", "Sequence Break", cells, entry.Span));
            }
            else if (entry is ResourceEntrySyntax resource)
            {
                var values = new Dictionary<string, PropertyAssignmentSyntax>(StringComparer.Ordinal);
                foreach (var property in resource.Properties) values[property.Name] = property;
                foreach (var column in columns)
                {
                    values.TryGetValue(column, out var property);
                    cells.Add(new LevelTableCell(column, property?.Expression ?? string.Empty, property?.ExpressionSpan));
                }
                rows.Add(new LevelTableRow(resource.Name, "Resource", cells, entry.Span));
            }
        }

        return new LevelTable(linkedLevel.LevelSet.Name, columns, rows, linkedLevel.Groups);
    }
}
