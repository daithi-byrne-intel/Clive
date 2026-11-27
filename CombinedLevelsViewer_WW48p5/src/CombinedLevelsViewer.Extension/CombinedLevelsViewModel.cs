using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using CombinedLevelsViewer.Core;
using Microsoft.VisualStudio.Extensibility.UI;

namespace CombinedLevelsViewer.Extension;

[DataContract]
internal sealed class CombinedLevelsViewModel : NotifyPropertyChangedObject, IDisposable
{
    private readonly PairedDocumentService? service;
    private string searchText = string.Empty;
    private LevelSetItem? selectedLevel;
    private TableRowItem? selectedRow;
    private string status = string.Empty;

    public CombinedLevelsViewModel()
    {
        FilteredLevelSets = new ObservableCollection<LevelSetItem>();
        Columns = new ObservableCollection<string>();
        Rows = new ObservableCollection<TableRowItem>();
        ContextItems = new ObservableCollection<ContextItem>();
        try
        {
            service = ViewerSession.GetOrCreateService();
            service.SnapshotChanged += OnSnapshotChanged;
            ViewerSession.LevelSelectionRequested += OnLevelSelectionRequested;
            RefreshLevels(service.Snapshot);
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
    }

    [DataMember]
    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value ?? string.Empty)) ApplyFilter();
        }
    }

    [DataMember]
    public ObservableCollection<LevelSetItem> FilteredLevelSets { get; }

    [DataMember]
    public LevelSetItem? SelectedLevel
    {
        get => selectedLevel;
        set
        {
            if (SetProperty(ref selectedLevel, value)) PopulateSelectedLevel();
        }
    }

    [DataMember]
    public ObservableCollection<string> Columns { get; }

    [DataMember]
    public ObservableCollection<TableRowItem> Rows { get; }

    [DataMember]
    public TableRowItem? SelectedRow
    {
        get => selectedRow;
        set
        {
            if (SetProperty(ref selectedRow, value) && value != null && SelectedLevel != null)
            {
                ViewerSession.NavigateToSource(PairedDocumentKind.Levels, value.SourceRow.SourceSpan, SelectedLevel.Name);
            }
        }
    }

    [DataMember]
    public ObservableCollection<ContextItem> ContextItems { get; }

    [DataMember]
    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    private void OnSnapshotChanged(object? sender, SnapshotChangedEventArgs args) => RefreshLevels(args.Snapshot);

    private void OnLevelSelectionRequested(object? sender, string levelName)
    {
        foreach (var item in FilteredLevelSets)
        {
            if (string.Equals(item.Name, levelName, StringComparison.Ordinal))
            {
                SelectedLevel = item;
                return;
            }
        }
    }

    private void RefreshLevels(PairedDocumentSnapshot snapshot)
    {
        var selectedName = SelectedLevel?.Name;
        allLevelSets.Clear();
        foreach (var linkedLevel in snapshot.LastValidModel.LevelSets)
        {
            allLevelSets.Add(new LevelSetItem(linkedLevel));
        }
        ApplyFilter(selectedName);
        Status = snapshot.CurrentDiagnostics.Count == 0
            ? allLevelSets.Count + " level sets loaded" + (snapshot.IsDirty ? " (modified)" : string.Empty)
            : snapshot.CurrentDiagnostics.Count + " diagnostic(s); table shows the last valid model.";
    }

    private readonly List<LevelSetItem> allLevelSets = new List<LevelSetItem>();

    private void ApplyFilter(string? preferredSelection = null)
    {
        preferredSelection ??= SelectedLevel?.Name;
        FilteredLevelSets.Clear();
        foreach (var item in allLevelSets)
        {
            if (SearchText.Length == 0 || item.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                FilteredLevelSets.Add(item);
            }
        }

        LevelSetItem? selection = null;
        foreach (var item in FilteredLevelSets)
        {
            if (string.Equals(item.Name, preferredSelection, StringComparison.Ordinal)) selection = item;
        }
        SelectedLevel = selection ?? (FilteredLevelSets.Count > 0 ? FilteredLevelSets[0] : null);
    }

    private void PopulateSelectedLevel()
    {
        Columns.Clear();
        Rows.Clear();
        ContextItems.Clear();
        if (SelectedLevel == null) return;

        var table = LevelTableProjector.Project(SelectedLevel.LinkedLevel);
        Columns.Add("Name");
        foreach (var column in table.Columns) Columns.Add(column);
        foreach (var row in table.Rows) Rows.Add(new TableRowItem(row, OnLevelCellEdited));
        foreach (var group in table.Groups)
        {
            ContextItems.Add(ContextItem.ReadOnly(group.Group.Name + " → " + (group.SpecificationSet?.Name ?? "missing specification")));
            if (group.SpecificationSet != null)
            {
                foreach (var value in group.SpecificationSet.Values)
                {
                    ContextItems.Add(new ContextItem(value.Name, value.Expression, value.ExpressionSpan, OnSpecificationEdited));
                }
            }
            foreach (var condition in group.TestConditions)
            {
                ContextItems.Add(ContextItem.ReadOnly("  " + condition.Name + " [" + condition.Selector + "]"));
            }
        }
        if (table.Groups.Count == 0) ContextItems.Add(ContextItem.ReadOnly("No linked test-condition group"));
    }

    private void OnLevelCellEdited(TableCellItem cell, string value)
    {
        if (service == null || cell.SourceCell.EditableSpan == null) return;
        if (!service.ApplyExpressionEdit(PairedDocumentKind.Levels, cell.SourceCell.EditableSpan.Value, value))
        {
            Status = "The value cannot contain a semicolon or newline.";
        }
    }

    private void OnSpecificationEdited(ContextItem item, string value)
    {
        if (service == null || item.EditableSpan == null) return;
        if (!service.ApplyExpressionEdit(PairedDocumentKind.TestConditions, item.EditableSpan.Value, value))
        {
            Status = "The expression cannot contain a semicolon or newline.";
        }
    }

    public void Dispose()
    {
        if (service != null) service.SnapshotChanged -= OnSnapshotChanged;
        ViewerSession.LevelSelectionRequested -= OnLevelSelectionRequested;
    }
}

[DataContract]
internal sealed class LevelSetItem
{
    public LevelSetItem(LinkedLevelSet linkedLevel)
    {
        LinkedLevel = linkedLevel;
        Name = linkedLevel.LevelSet.Name;
        LinkState = linkedLevel.Groups.Count == 0 ? "Unlinked" : "Linked";
    }

    [DataMember]
    public string Name { get; }

    [DataMember]
    public string LinkState { get; }

    public LinkedLevelSet LinkedLevel { get; }
}

[DataContract]
internal sealed class TableRowItem
{
    public TableRowItem(LevelTableRow row, Action<TableCellItem, string> edit)
    {
        SourceRow = row;
        Name = row.Name;
        Kind = row.Kind;
        Cells = new ObservableCollection<TableCellItem>();
        foreach (var cell in row.Cells) Cells.Add(new TableCellItem(cell, edit));
    }

    [DataMember]
    public string Name { get; }

    [DataMember]
    public string Kind { get; }

    [DataMember]
    public ObservableCollection<TableCellItem> Cells { get; }

    public LevelTableRow SourceRow { get; }
}

[DataContract]
internal sealed class TableCellItem : NotifyPropertyChangedObject
{
    private readonly Action<TableCellItem, string> edit;
    private string value;

    public TableCellItem(LevelTableCell cell, Action<TableCellItem, string> edit)
    {
        SourceCell = cell;
        this.edit = edit;
        Column = cell.Column;
        value = cell.Value;
    }

    [DataMember]
    public string Column { get; }

    [DataMember]
    public string Value
    {
        get => value;
        set
        {
            if (IsReadOnly || !SetProperty(ref this.value, value ?? string.Empty)) return;
            edit(this, this.value);
        }
    }

    [DataMember]
    public bool IsReadOnly => SourceCell.EditableSpan == null;

    public LevelTableCell SourceCell { get; }
}

[DataContract]
internal sealed class ContextItem : NotifyPropertyChangedObject
{
    private readonly Action<ContextItem, string>? edit;
    private string value;

    public ContextItem(string label, string value, TextSpan editableSpan, Action<ContextItem, string> edit)
    {
        Label = label;
        this.value = value;
        EditableSpan = editableSpan;
        this.edit = edit;
    }

    private ContextItem(string label)
    {
        Label = label;
        value = string.Empty;
    }

    public static ContextItem ReadOnly(string label) => new ContextItem(label);

    [DataMember]
    public string Label { get; }

    [DataMember]
    public string Value
    {
        get => value;
        set
        {
            if (IsReadOnly || !SetProperty(ref this.value, value ?? string.Empty)) return;
            edit?.Invoke(this, this.value);
        }
    }

    [DataMember]
    public bool IsReadOnly => EditableSpan == null;

    public TextSpan? EditableSpan { get; }
}
