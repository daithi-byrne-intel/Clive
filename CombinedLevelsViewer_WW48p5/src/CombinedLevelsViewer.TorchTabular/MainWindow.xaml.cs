using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CombinedLevelsViewer.Core;

namespace CombinedLevelsViewer.TorchTabular;

internal sealed record ContextRow(string Label, string Value);

/// <summary>
/// A node of the level tree: a level set (raw, editable expressions) or one of its test conditions
/// (values resolved for that condition's selector, read-only).
/// </summary>
internal sealed record LevelNode(string Level, string? Group, string? Condition, string? Selector)
{
    public bool IsSelector => Condition != null;

    public string Key => IsSelector ? Level + "\u001f" + Group + "\u001f" + Condition : Level;
}

public partial class MainWindow : Window
{
    private const string SelfTestSwitch = "--selftest";
    private const string SelfTestSaveSwitch = "--selftest-save";

    private readonly HashSet<string> expandedLevels = new(StringComparer.Ordinal);
    private PairedDocumentService? service;
    private TorchGridBridge? bridge;
    private UserVarsDocument userVars = UserVarsDocument.Empty;
    private ParameterResolver resolver = new(UserVarsDocument.Empty);
    private DuplicateAnalysis duplicates = DuplicateAnalysis.Empty;
    private UserVarsDocument? analysedUserVars;
    private long analysedRevision = -1;
    private string resolutionSummary = string.Empty;
    private string? userVarsPath;
    private string? userVarsProblem;
    private bool gridReady;
    private bool updatingTree;
    private bool selfTesting;
    private bool selfTestSave;
    private string? editNote;
    private TaskCompletionSource<bool>? pendingCommit;
    private string? pendingCommitToken;
    private string? rejectedEdit;
    private bool closeAfterCommit;
    private LevelNode? selectedNode;
    private LevelNode? displayedNode;
    private string displayedShape = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToList();
        string? selfTestOutput = null;
        selfTestSave = arguments.Remove(SelfTestSaveSwitch);
        var selfTestIndex = arguments.IndexOf(SelfTestSwitch);
        if (selfTestIndex >= 0 && selfTestIndex + 1 < arguments.Count)
        {
            selfTestOutput = arguments[selfTestIndex + 1];
            arguments.RemoveRange(selfTestIndex, 2);
        }

        try
        {
            var root = RepositoryLocator.FindRoot(arguments.FirstOrDefault());
            service = new PairedDocumentService(RepositoryLocator.LevelsPath(root), RepositoryLocator.TestConditionsPath(root));
            service.SnapshotChanged += (_, args) => Dispatcher.InvokeAsync(() => RefreshLevels(args.Snapshot));
            service.ExternalChangeDetected += (_, args) => Dispatcher.InvokeAsync(
                () => SetStatus(Path.GetFileName(args.Path) + " changed on disk. Use Reload from disk to pick up the change."));
            Title = "Clive - " + root;
            userVarsPath = RepositoryLocator.UserVarsPath(root);
            LoadUserVars();

            bridge = new TorchGridBridge(
                GridView,
                TorchInstall.FindWebRoot(),
                Path.Combine(AppContext.BaseDirectory, "Web", "Html", "LevelsComponents"));
            bridge.EventReceived += OnGridEvent;
            await bridge.InitializeAsync();
            gridReady = true;
            RefreshLevels(service.Snapshot);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }

        if (selfTestOutput != null) await RunSelfTestAsync(selfTestOutput);
    }

    private void LoadUserVars()
    {
        userVars = UserVarsDocument.Empty;
        userVarsProblem = null;
        if (userVarsPath == null) return;
        try
        {
            userVars = UserVarsParser.Parse(File.ReadAllText(userVarsPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            userVarsProblem = "Uservars not loaded: " + exception.Message;
        }
    }

    /// <summary>Rebuilds the tree: one node per level set, with one child per test condition (selector).</summary>
    private void RefreshLevels(PairedDocumentSnapshot snapshot)
    {
        resolver = new ParameterResolver(userVars);
        if (analysedRevision != snapshot.Revision || !ReferenceEquals(analysedUserVars, userVars))
        {
            // Whole-file checks are only redone when the text or the uservars changed, not on every filter keystroke.
            duplicates = DuplicateAnalysis.Analyze(snapshot.LastValidModel, userVars);
            resolutionSummary = DescribeResolution(snapshot);
            analysedRevision = snapshot.Revision;
            analysedUserVars = userVars;
        }

        var duplicateLevels = 0;
        var preferredKey = selectedNode?.Key;
        var filter = FilterBox.Text.Trim();

        updatingTree = true;
        try
        {
            LevelTree.Items.Clear();
            TreeViewItem? toSelect = null;
            foreach (var level in snapshot.LastValidModel.LevelSets)
            {
                var levelName = level.LevelSet.Name;
                var children = new List<LevelNode>();
                foreach (var group in level.Groups)
                {
                    foreach (var condition in group.TestConditions)
                    {
                        children.Add(new LevelNode(levelName, group.Group.Name, condition.Name, condition.Selector));
                    }
                }

                var levelMatches = filter.Length == 0 || levelName.Contains(filter, StringComparison.OrdinalIgnoreCase);
                var childMatches = !levelMatches && children.Any(child => child.Condition!.Contains(filter, StringComparison.OrdinalIgnoreCase));
                if (!levelMatches && !childMatches) continue;

                var levelNode = new LevelNode(levelName, null, null, null);
                var levelItem = new TreeViewItem { Header = levelName, Tag = levelNode };
                var levelDuplicates = duplicates.ForLevel(levelName);
                if (levelDuplicates.Count > 0)
                {
                    duplicateLevels++;
                    levelItem.Background = CompareWindow.DuplicateBrush;
                    levelItem.ToolTip = DescribeLevelDuplicates(levelDuplicates) + "\nRight-click to compare.";
                }
                levelItem.ContextMenu = CreateCompareMenu(levelNode, levelDuplicates.Count > 0);
                if (levelNode.Key == preferredKey) toSelect = levelItem;
                foreach (var child in children)
                {
                    var conditionDuplicates = duplicates.ForCondition(child.Level, child.Group!, child.Condition!);
                    var childItem = new TreeViewItem
                    {
                        Header = child.Condition,
                        Tag = child,
                        ToolTip = "Selector " + child.Selector + " of test-condition group " + child.Group
                            + (conditionDuplicates.Count > 0
                                ? "\nSame values as: " + string.Join(", ", conditionDuplicates.Select(other => other.Condition)) + "\nRight-click to compare."
                                : string.Empty),
                        ContextMenu = CreateCompareMenu(child, conditionDuplicates.Count > 0),
                    };
                    if (conditionDuplicates.Count > 0) childItem.Background = CompareWindow.DuplicateBrush;
                    levelItem.Items.Add(childItem);
                    if (child.Key == preferredKey)
                    {
                        toSelect = childItem;
                        expandedLevels.Add(levelName);
                    }
                }

                levelItem.IsExpanded = expandedLevels.Contains(levelName) || childMatches;
                levelItem.Expanded += (_, _) => expandedLevels.Add(levelName);
                levelItem.Collapsed += (_, _) => expandedLevels.Remove(levelName);
                LevelTree.Items.Add(levelItem);
            }

            toSelect ??= LevelTree.Items.Count > 0 ? (TreeViewItem)LevelTree.Items[0] : null;
            if (toSelect != null) toSelect.IsSelected = true;
            selectedNode = toSelect?.Tag as LevelNode;
        }
        finally
        {
            updatingTree = false;
        }

        ShowSelectedNode(snapshot);
        var summary = snapshot.CurrentDiagnostics.Count == 0
            ? snapshot.LastValidModel.LevelSets.Count + " level sets loaded"
            : snapshot.CurrentDiagnostics.Count + " diagnostic(s), first: " + snapshot.CurrentDiagnostics[0].Message;
        SetStatus((editNote != null ? editNote + " " : string.Empty) + summary + (snapshot.IsDirty ? " (modified, not saved)" : string.Empty) + ". " + resolutionSummary
            + (filter.Length == 0 ? " Duplicates: " + duplicateLevels + " level sets produce the same values as another (highlighted)." : string.Empty));
        editNote = null;
    }

    private static string DescribeLevelDuplicates(IReadOnlyList<LevelDuplicate> levelDuplicates)
    {
        var lines = new List<string>();
        void Add(LevelDuplicateRelation relation, string label)
        {
            var names = levelDuplicates.Where(item => item.Relation == relation).Select(item => item.OtherLevel).ToList();
            if (names.Count > 0) lines.Add(label + string.Join(", ", names));
        }

        Add(LevelDuplicateRelation.Identical, "Same selectors and values as: ");
        Add(LevelDuplicateRelation.ContainedIn, "All of its selectors exist with the same values in: ");
        Add(LevelDuplicateRelation.Contains, "Also covers every selector of: ");
        return string.Join("\n", lines);
    }

    private ContextMenu CreateCompareMenu(LevelNode node, bool hasDuplicates)
    {
        var item = new MenuItem { Header = hasDuplicates ? "Compare with duplicates..." : "No duplicates to compare", IsEnabled = hasDuplicates };
        item.Click += (_, _) => ShowComparison(node);
        var menu = new ContextMenu();
        menu.Items.Add(item);
        return menu;
    }

    private void ShowComparison(LevelNode node)
    {
        if (service == null) return;
        var comparisons = BuildComparisons(node, service.Snapshot);
        if (comparisons.Count == 0) return;
        new CompareWindow(this, node.Condition ?? node.Level, comparisons).ShowDialog();
    }

    private List<Comparison> BuildComparisons(LevelNode node, PairedDocumentSnapshot snapshot)
    {
        var comparisons = new List<Comparison>();
        if (node.IsSelector)
        {
            var left = new ConditionReference(node.Level, node.Group!, node.Condition!, node.Selector!);
            foreach (var other in duplicates.ForCondition(node.Level, node.Group!, node.Condition!))
            {
                var right = other;
                comparisons.Add(new Comparison(
                    right.Condition + "  (level set " + right.Level + ", selector " + right.Selector + ")",
                    left.Condition,
                    right.Condition,
                    () => CompareConditions(snapshot, left, right)));
            }
            return comparisons;
        }

        foreach (var duplicate in duplicates.ForLevel(node.Level))
        {
            var other = duplicate;
            var relation = other.Relation switch
            {
                LevelDuplicateRelation.Identical => "same selectors and values",
                LevelDuplicateRelation.ContainedIn => "has every selector of " + node.Level + ", and more",
                _ => "all of its selectors are covered by " + node.Level,
            };
            comparisons.Add(new Comparison(
                other.OtherLevel + "  (" + relation + "; " + other.SharedSelectors.Count + " shared selector(s))",
                node.Level,
                other.OtherLevel,
                () =>
                {
                    var rows = new List<CompareRow>();
                    foreach (var selector in other.SharedSelectors)
                    {
                        var left = FindCondition(snapshot, node.Level, selector);
                        var right = FindCondition(snapshot, other.OtherLevel, selector);
                        if (left != null && right != null) rows.AddRange(CompareConditions(snapshot, left, right));
                    }
                    return rows;
                }));
        }
        return comparisons;
    }

    private static ConditionReference? FindCondition(PairedDocumentSnapshot snapshot, string level, string selector)
    {
        var linked = FindLevel(snapshot, level);
        if (linked == null) return null;
        foreach (var group in linked.Groups)
        {
            foreach (var condition in group.TestConditions)
            {
                if (string.Equals(condition.Selector, selector, StringComparison.Ordinal)) return new ConditionReference(level, group.Group.Name, condition.Name, selector);
            }
        }
        return null;
    }

    /// <summary>Compares two test conditions cell by cell: the value each produces and how it is defined.</summary>
    private List<CompareRow> CompareConditions(PairedDocumentSnapshot snapshot, ConditionReference left, ConditionReference right)
    {
        var rows = new List<CompareRow>();
        var leftLevel = FindLevel(snapshot, left.Level);
        var rightLevel = FindLevel(snapshot, right.Level);
        if (leftLevel == null || rightLevel == null) return rows;
        var leftTable = LevelTableProjector.Project(leftLevel);
        var rightTable = LevelTableProjector.Project(rightLevel);
        var leftGroup = FindGroup(leftTable, left.Group);
        var rightGroup = FindGroup(rightTable, right.Group);
        if (leftGroup == null || rightGroup == null) return rows;

        var columns = leftTable.Columns.Concat(rightTable.Columns.Where(column => !leftTable.Columns.Contains(column))).ToList();
        var selector = left.Selector == right.Selector ? left.Selector : left.Selector + " / " + right.Selector;
        for (var index = 0; index < Math.Max(leftTable.Rows.Count, rightTable.Rows.Count); index++)
        {
            var leftRow = index < leftTable.Rows.Count ? leftTable.Rows[index] : null;
            var rightRow = index < rightTable.Rows.Count ? rightTable.Rows[index] : null;
            var resource = leftRow != null && rightRow != null && leftRow.Name != rightRow.Name
                ? leftRow.Name + " / " + rightRow.Name
                : (leftRow ?? rightRow)!.Name;
            foreach (var column in columns)
            {
                var leftExpression = leftRow?.Cells.FirstOrDefault(cell => cell.Column == column)?.Value ?? string.Empty;
                var rightExpression = rightRow?.Cells.FirstOrDefault(cell => cell.Column == column)?.Value ?? string.Empty;
                if (leftExpression.Length == 0 && rightExpression.Length == 0) continue;

                var (leftValue, leftHow) = Explain(leftExpression, column, leftGroup, left.Selector);
                var (rightValue, rightHow) = Explain(rightExpression, column, rightGroup, right.Selector);
                var defined = ParameterResolver.NormalizeValue(leftValue) != ParameterResolver.NormalizeValue(rightValue)
                    ? CompareRow.ValuesDiffer
                    : SameDefinition(leftHow, rightHow) ? CompareRow.SameWay : CompareRow.Differently;
                rows.Add(new CompareRow(selector, resource, column, leftValue, rightValue, leftHow, rightHow, defined));
            }
        }
        return rows;
    }

    /// <summary>
    /// True when two definition chains use the same names and values. Where the selector sits in a value list
    /// ("entry 1 of 4" against "entry 1 of 5") is not a difference in definition.
    /// </summary>
    private static bool SameDefinition(string leftHow, string rightHow)
    {
        const string pattern = @"\s*\[[^\]]*, entry \d+ of \d+\]";
        return System.Text.RegularExpressions.Regex.Replace(leftHow, pattern, string.Empty)
            == System.Text.RegularExpressions.Regex.Replace(rightHow, pattern, string.Empty);
    }

    /// <summary>The value of one cell for a selector, and the chain of definitions that leads to it.</summary>
    private (string Value, string How) Explain(string expression, string column, LinkedTestConditionGroup group, string selector)
    {
        if (expression.Length == 0) return (string.Empty, string.Empty);
        if (!ParameterResolver.IsParameterProperty(column)) return (expression, expression);
        var resolved = resolver.Resolve(expression, group, selector);
        var how = resolved.Changed || resolved.Problem != null
            ? string.Join("  \u2192  ", new[] { resolved.Expression }.Concat(resolved.Steps))
            : resolved.Text;
        return (resolved.Problem != null ? ParameterResolver.UnresolvedPrefix + resolved.Problem : resolved.Text, how);
    }

    private string DescribeResolution(PairedDocumentSnapshot snapshot)
    {
        if (userVarsProblem != null) return userVarsProblem;
        var clamps = ClampResolver.Audit(snapshot.LastValidModel, userVars);
        var parameters = ParameterResolver.Audit(snapshot.LastValidModel, userVars);
        var clampText = clamps.Unresolved == 0
            ? "Clamps: all " + clamps.References + " match a uservar exactly."
            : "Clamps: " + clamps.Unresolved + " of " + clamps.References + " have no exact uservar match.";
        var parameterText = parameters.Unresolved == 0
            ? "Parameters: all " + parameters.References + " references resolve for every test condition."
            : "Parameters: " + parameters.Unresolved + " of " + parameters.References + " references do not resolve, first: " + parameters.Problems[0];
        return clampText + " " + parameterText;
    }

    private void ShowSelectedNode(PairedDocumentSnapshot snapshot)
    {
        var node = selectedNode;
        LevelTitle.Text = node?.Level ?? string.Empty;
        ContextList.Items.Clear();

        var linked = FindLevel(snapshot, node?.Level);
        if (node == null || linked == null)
        {
            if (gridReady && displayedNode != null) SendCommand("FillGridCommand", new { Records = Array.Empty<GridRecord>(), GridState = (object?)null, DebugEnabled = false });
            displayedNode = null;
            displayedShape = string.Empty;
            return;
        }

        var table = LevelTableProjector.Project(linked);
        PopulateContext(table);

        var group = node.IsSelector ? FindGroup(table, node.Group) : null;
        List<GridRecord> records;
        int unresolved;
        string shape;
        if (group != null)
        {
            // A test condition is selected: every parameter is replaced by its value for that selector.
            // An edit is written to the definition that holds the value (the level or the specification set).
            records = LevelsRecordsExtractor.ExtractResolved(table, group, node.Selector!, resolver, snapshot.TestConditionsText, out unresolved);
            var values = records.SelectMany(record => record.CellsData).Where(pair => pair.Key != LevelsRecordsExtractor.RowNameColumn && pair.Value.Data.Length > 0).ToList();
            var editableValues = values.Count(pair => (pair.Value.CellOptions & 1) != 0);
            LevelTitle.Text = node.Level + "  \u2014  " + node.Condition + ": values for selector " + node.Selector
                + " (specification set " + (group.SpecificationSet?.Name ?? group.Group.SpecificationSetName) + ")"
                + "  \u2014  " + editableValues + " of " + values.Count + " values can be edited here; grey values are calculated or come from uservars (double-click one to see why)"
                + (unresolved > 0 ? "  \u2014  " + unresolved + " value(s) not resolved" : string.Empty);
            shape = "resolved|";
        }
        else
        {
            records = LevelsRecordsExtractor.Extract(table, userVars, out unresolved);
            if (unresolved > 0) LevelTitle.Text = node.Level + "  \u2014  " + unresolved + " clamp value(s) without an exact uservar match";
            shape = "raw|";
        }

        if (!gridReady) return;

        shape += table.Rows.Count + "|" + string.Join("\u001f", table.Columns);
        if (displayedNode?.Key == node.Key && displayedShape == shape)
        {
            // Same rows and columns: push new cell values through Torch's base UpdateGridCommand
            // so scroll position, sorting and filters are kept.
            SendCommand("UpdateGridCommand", new { RecordsToAdd = Array.Empty<GridRecord>(), RecordsToUpdate = records, RecordsToRemove = Array.Empty<GridRecord>() });
        }
        else
        {
            SendCommand("FillGridCommand", new { Records = records, GridState = (object?)null, DebugEnabled = false });
        }

        displayedNode = node;
        displayedShape = shape;
    }

    private void PopulateContext(LevelTable table)
    {
        foreach (var group in table.Groups)
        {
            ContextList.Items.Add(new ContextRow(group.Group.Name + " \u2192 " + (group.SpecificationSet?.Name ?? "missing specification"), string.Empty));
            if (group.SpecificationSet != null)
            {
                foreach (var value in group.SpecificationSet.Values) ContextList.Items.Add(new ContextRow("    " + value.Name, value.Expression));
            }
            foreach (var condition in group.TestConditions) ContextList.Items.Add(new ContextRow("    " + condition.Name, "[" + condition.Selector + "]"));
        }

        if (table.Groups.Count == 0) ContextList.Items.Add(new ContextRow("No linked test-condition group", string.Empty));
    }

    private void OnGridEvent(object? sender, GridEvent gridEvent)
    {
        switch (gridEvent.Name)
        {
            case "CellEditEvent":
                if (gridEvent.Args.ValueKind != JsonValueKind.Object || !gridEvent.Args.TryGetProperty("CellEditArgs", out var edits) || edits.ValueKind != JsonValueKind.Array) return;
                foreach (var edit in edits.EnumerateArray())
                {
                    var problem = ApplyLevelEdit(ReadString(edit, "RowId"), ReadString(edit, "ColumnName"), ReadString(edit, "NewValue"));
                    if (problem != null)
                    {
                        // Torch's grid is read-only-edit, so a rejected edit simply keeps the old value.
                        rejectedEdit = problem;
                        SetStatus(problem);
                        return;
                    }
                }
                break;
            case "EditCommittedEvent":
                if (pendingCommit != null && ReadString(gridEvent.Args, "Token") == pendingCommitToken) pendingCommit.TrySetResult(true);
                break;
            case "CellNotEditableEvent":
                SetStatus(ExplainNotEditable(ReadString(gridEvent.Args, "RowId"), ReadString(gridEvent.Args, "ColumnName")));
                break;
            case "ExceptionEvent":
                SetStatus("Grid error: " + gridEvent.Args.GetRawText());
                break;
        }
    }

    /// <summary>
    /// Finishes a cell editor that is still open, as pressing Enter would, and waits until the typed value has
    /// been applied. Returns false when that value was rejected, in which case the status bar says why.
    /// </summary>
    private async Task<bool> CommitPendingEditAsync()
    {
        if (!gridReady || bridge == null) return true;
        var completion = new TaskCompletionSource<bool>();
        var token = Guid.NewGuid().ToString("N");
        pendingCommit = completion;
        pendingCommitToken = token;
        rejectedEdit = null;
        try
        {
            await bridge.RunCommandAsync("CommitEditCommand", new { Token = token });
            await Task.WhenAny(completion.Task, Task.Delay(3000));
        }
        catch (Exception exception)
        {
            SetStatus("The value being edited could not be applied: " + exception.Message);
            return false;
        }
        finally
        {
            if (ReferenceEquals(pendingCommit, completion)) pendingCommit = null;
        }

        return rejectedEdit == null;
    }

    /// <summary>Why the double-clicked cell cannot be edited in the current view.</summary>
    private string ExplainNotEditable(string? rowId, string? column)
    {
        const string prefix = "Not editable: ";
        if (service == null || displayedNode == null || column == null) return prefix + "no level is displayed.";
        if (column == LevelsRecordsExtractor.RowNameColumn) return prefix + "resource and sequence-break names are changed in the .lvl source.";
        if (column.EndsWith(LevelsRecordsExtractor.UserVarColumnSuffix, StringComparison.Ordinal)) return prefix + "this column shows the uservar from UservarDefinitions.usrv; edit the clamp column next to it.";

        var snapshot = service.Snapshot;
        var linked = FindLevel(snapshot, displayedNode.Level);
        if (linked == null) return prefix + "the level is no longer present.";
        var table = LevelTableProjector.Project(linked);
        if (!LevelsRecordsExtractor.TryParseRowId(rowId, out var rowIndex) || rowIndex >= table.Rows.Count) return prefix + "the row no longer exists.";
        var cell = table.Rows[rowIndex].Cells.FirstOrDefault(candidate => string.Equals(candidate.Column, column, StringComparison.Ordinal));
        if (cell == null || cell.Value.Length == 0) return prefix + "'" + column + "' is not set on " + table.Rows[rowIndex].Name + "; add it in the .lvl source first.";

        var group = displayedNode.IsSelector ? FindGroup(table, displayedNode.Group) : null;
        if (group == null) return prefix + "this value cannot be changed from the table.";
        var target = new ParameterResolver(userVars).FindEditTarget(cell.Value, group, displayedNode.Selector!, snapshot.TestConditionsText);
        return prefix + (target.Reason ?? "this value cannot be changed from the table.")
            + " Select the level set '" + displayedNode.Level + "' in the tree to change which parameter this cell uses (" + cell.Value + ").";
    }

    /// <summary>Applies one grid edit to the .lvl text. Returns a message when the edit is rejected.</summary>
    private string? ApplyLevelEdit(string? rowId, string? column, string? newValue)
    {
        if (service == null || displayedNode == null || column == null) return "No level is displayed.";
        if (displayedNode.IsSelector) return ApplyResolvedEdit(displayedNode, rowId, column, newValue);
        if (column.EndsWith(LevelsRecordsExtractor.UserVarColumnSuffix, StringComparison.Ordinal)) return "Uservar values are read from UservarDefinitions.usrv and cannot be edited here.";

        // Re-project from the latest text for every edit: an earlier edit in the same paste shifts later spans.
        var snapshot = service.Snapshot;
        var linked = FindLevel(snapshot, displayedNode.Level);
        if (linked == null) return "Level '" + displayedNode.Level + "' is no longer present.";
        var table = LevelTableProjector.Project(linked);
        if (!LevelsRecordsExtractor.TryParseRowId(rowId, out var rowIndex) || rowIndex >= table.Rows.Count) return "The edited row no longer exists.";

        var cell = table.Rows[rowIndex].Cells.FirstOrDefault(candidate => string.Equals(candidate.Column, column, StringComparison.Ordinal));
        if (cell?.EditableSpan == null) return "'" + column + "' is not set on " + table.Rows[rowIndex].Name + "; add it in the .lvl source first.";

        var value = (newValue ?? string.Empty).Trim();
        if (value.Length == 0) return "A value cannot be empty.";
        return ApplyLevelTextEdit(snapshot, cell, value);
    }

    /// <summary>Replaces the expression of one level cell in the .lvl text.</summary>
    private string? ApplyLevelTextEdit(PairedDocumentSnapshot snapshot, LevelTableCell cell, string value)
    {
        if (cell.EditableSpan == null) return "'" + cell.Column + "' is not set on this row; add it in the .lvl source first.";
        if (value == cell.Value) return null;

        var span = cell.EditableSpan.Value;
        var text = snapshot.LevelsText;
        if (span.Start < 0 || span.End > text.Length) return "The table is out of date; reload from disk.";

        // Refuse anything that would leave the file unparsable, so the table never works from stale positions.
        var candidateText = text.Substring(0, span.Start) + value + text.Substring(span.End);
        if (LevelsParser.Parse(candidateText).HasErrors) return "'" + value + "' is not a valid level expression; the edit was not applied.";

        return service!.ApplyExpressionEdit(PairedDocumentKind.Levels, span, value)
            ? null
            : "The value cannot contain a semicolon or a line break.";
    }

    /// <summary>
    /// Applies an edit made in the per-selector view. The new value is written where the shown value is defined:
    /// in the level when the level holds a literal, otherwise in the specification-set entry for the selector.
    /// </summary>
    private string? ApplyResolvedEdit(LevelNode node, string? rowId, string column, string? newValue)
    {
        var snapshot = service!.Snapshot;
        var linked = FindLevel(snapshot, node.Level);
        if (linked == null) return "Level '" + node.Level + "' is no longer present.";
        var table = LevelTableProjector.Project(linked);
        var group = FindGroup(table, node.Group);
        if (group == null) return "Test-condition group '" + node.Group + "' is no longer linked to this level.";
        if (!LevelsRecordsExtractor.TryParseRowId(rowId, out var rowIndex) || rowIndex >= table.Rows.Count) return "The edited row no longer exists.";

        var cell = table.Rows[rowIndex].Cells.FirstOrDefault(candidate => string.Equals(candidate.Column, column, StringComparison.Ordinal));
        if (cell == null || cell.Value.Length == 0) return "'" + column + "' is not set on " + table.Rows[rowIndex].Name + "; add it in the .lvl source first.";

        var value = (newValue ?? string.Empty).Trim();
        if (value.Length == 0) return "A value cannot be empty.";
        if (!ParameterResolver.IsParameterProperty(column)) return ApplyLevelTextEdit(snapshot, cell, value);

        // Trace from the latest text for every edit: an earlier edit in the same paste shifts later positions.
        var target = new ParameterResolver(userVars).FindEditTarget(cell.Value, group, node.Selector!, snapshot.TestConditionsText);
        if (!target.Editable) return target.Reason;
        if (target.Kind == PairedDocumentKind.Levels) return ApplyLevelTextEdit(snapshot, cell, value);

        var text = snapshot.TestConditionsText;
        var current = text.Substring(target.Span.Start, target.Span.Length);
        if (value == current) return null;
        if (SemanticLinker.SplitTopLevelValues(value).Count != 1) return "Enter a single value; '" + value + "' would change the number of values listed for " + target.Owner + ".";

        var candidateText = text.Substring(0, target.Span.Start) + value + text.Substring(target.Span.End);
        if (TcgParser.Parse(candidateText).HasErrors) return "'" + value + "' is not a valid value for " + target.Owner + "; the edit was not applied.";
        if (!service.ApplyExpressionEdit(PairedDocumentKind.TestConditions, target.Span, value)) return "The value cannot contain a semicolon or a line break.";

        editNote = target.Owner + " changed from " + current + " to " + value
            + (target.AppliesToAllSelectors ? " for all selectors." : " for selector " + node.Selector + ".");
        return null;
    }

    private static LinkedLevelSet? FindLevel(PairedDocumentSnapshot snapshot, string? name)
    {
        if (name == null) return null;
        foreach (var level in snapshot.LastValidModel.LevelSets)
        {
            if (string.Equals(level.LevelSet.Name, name, StringComparison.Ordinal)) return level;
        }
        return null;
    }

    private static LinkedTestConditionGroup? FindGroup(LevelTable table, string? name)
    {
        foreach (var group in table.Groups)
        {
            if (string.Equals(group.Group.Name, name, StringComparison.Ordinal)) return group;
        }
        return null;
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText(),
        };
    }

    private async void SendCommand(string name, object args)
    {
        if (bridge == null) return;
        try
        {
            await bridge.RunCommandAsync(name, args);
        }
        catch (Exception exception)
        {
            SetStatus("Grid command " + name + " failed: " + exception.Message);
        }
    }

    private async void OnLevelSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (updatingTree || service == null) return;
        selectedNode = (LevelTree.SelectedItem as TreeViewItem)?.Tag as LevelNode;

        // A cell editor may still be open on the table being left; apply it there before showing the new one.
        await CommitPendingEditAsync();
        ShowSelectedNode(service.Snapshot);
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (service != null) RefreshLevels(service.Snapshot);
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveAsync();

    /// <summary>Applies a value that is still being typed, then saves. Nothing is saved if that value is rejected.</summary>
    private async Task<bool> SaveAsync()
    {
        if (!await CommitPendingEditAsync())
        {
            SetStatus("Not saved. The value being edited was rejected: " + (rejectedEdit ?? StatusText.Text));
            return false;
        }
        return TrySave();
    }

    private bool TrySave()
    {
        if (service == null) return false;
        try
        {
            service.Save();
            SetStatus("Saved " + Path.GetFileName(service.LevelsPath) + " and " + Path.GetFileName(service.TestConditionsPath) + ".");
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            SetStatus("Not saved: " + exception.Message);
            return false;
        }
    }

    private void OnReloadClick(object sender, RoutedEventArgs e)
    {
        if (service == null) return;
        if (service.Snapshot.IsDirty
            && MessageBox.Show(this, "Discard unsaved edits and reload both files from disk?", Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            LoadUserVars();
            service.ReloadFromDisk();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus("Reload failed: " + exception.Message);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!closeAfterCommit && gridReady && !selfTesting)
        {
            // A value may still be in an open cell editor. Apply it first, then come back here.
            e.Cancel = true;
            _ = CloseAfterCommitAsync();
            return;
        }
        closeAfterCommit = false;

        if (service != null && service.Snapshot.IsDirty && !selfTesting)
        {
            var answer = MessageBox.Show(this, "Save changes to the levels files before closing?", Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !TrySave()))
            {
                e.Cancel = true;
                return;
            }
        }

        service?.Dispose();
    }

    private async Task CloseAfterCommitAsync()
    {
        if (!await CommitPendingEditAsync())
        {
            SetStatus("Not closed. The value being edited was rejected: " + (rejectedEdit ?? StatusText.Text) + " The cell keeps its old value; edit it again, or close again to leave it as it is.");
            return;
        }
        closeAfterCommit = true;
        Close();
    }

    private void SetStatus(string message) => StatusText.Text = message;

    /// <summary>Selects the tree node with the given key, expanding its level. Returns false when it is not shown.</summary>
    private bool SelectNode(string key)
    {
        foreach (TreeViewItem levelItem in LevelTree.Items)
        {
            if ((levelItem.Tag as LevelNode)?.Key == key)
            {
                levelItem.IsSelected = true;
                return true;
            }
            foreach (TreeViewItem childItem in levelItem.Items)
            {
                if ((childItem.Tag as LevelNode)?.Key != key) continue;
                levelItem.IsExpanded = true;
                childItem.IsSelected = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Headless check used from the command line (--selftest &lt;file&gt;): reports what the Torch grid
    /// actually rendered, whether an edit posted by the grid comes back as an updated cell, and how clamps
    /// and parameters resolve, then exits. The edit stays in memory; nothing is saved.
    /// </summary>
    private async Task RunSelfTestAsync(string outputPath)
    {
        selfTesting = true;
        var report = new Dictionary<string, object?>
        {
            ["gridReady"] = gridReady,
            ["status"] = StatusText.Text,
            ["levelCount"] = LevelTree.Items.Count,
            ["displayedLevel"] = displayedNode?.Level,
        };

        try
        {
            if (gridReady && bridge != null)
            {
                await Task.Delay(2000);
                const string script = "JSON.stringify({ renderedRows: document.querySelectorAll('#myGrid .ag-center-cols-container .ag-row').length, headerCells: document.querySelectorAll('#myGrid .ag-header-cell').length })";
                var encoded = await bridge.EvaluateAsync(script);
                report["grid"] = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Deserialize<string>(encoded) ?? "{}");
                report["uiEdit"] = await RunUiEditAsync();
                report["openEditor"] = await RunOpenEditorAsync();
                report["editRoundTrip"] = await RunEditRoundTripAsync();
                report["clamps"] = await RunClampCheckAsync();
                report["parameters"] = await RunParameterCheckAsync();
                report["duplicates"] = RunDuplicateCheck();
            }
        }
        catch (Exception exception)
        {
            report["selfTestError"] = exception.ToString();
        }

        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Close();
    }

    private async Task<string?> ReadGridCellAsync(int rowIndex, string column)
    {
        // The grid only renders visible rows and columns, so scroll the cell into view first.
        var reveal = "import('../BaseComponents/Services/GridApiProvider.js').then(function (m) { var api = m.default.getGridApi(); var node = api.getRowNode("
            + JsonSerializer.Serialize(LevelsRecordsExtractor.ToRowId(rowIndex)) + "); if (node) { api.ensureNodeVisible(node); } api.ensureColumnVisible("
            + JsonSerializer.Serialize(column) + "); }); 0";
        await bridge!.EvaluateAsync(reveal);
        await Task.Delay(400);

        var probe = "(function () { var rows = document.querySelectorAll('#myGrid .ag-center-cols-container [row-id]'); for (var i = 0; i < rows.length; i++) { if (rows[i].getAttribute('row-id') !== "
            + JsonSerializer.Serialize(LevelsRecordsExtractor.ToRowId(rowIndex))
            + ") continue; var cells = rows[i].querySelectorAll('[col-id]'); for (var j = 0; j < cells.length; j++) { if (cells[j].getAttribute('col-id') === "
            + JsonSerializer.Serialize(column) + ") return cells[j].textContent; } } return null; })()";
        return JsonSerializer.Deserialize<string?>(await bridge!.EvaluateAsync(probe));
    }

    /// <summary>Double-clicks a cell, and if an editor opens, types a value and presses Enter. Returns what happened.</summary>
    private async Task<string> UiEditAsync(int rowIndex, string column, string typed)
    {
        await ReadGridCellAsync(rowIndex, column); // Scrolls the cell into view.
        var rowId = JsonSerializer.Serialize(LevelsRecordsExtractor.ToRowId(rowIndex));
        var columnId = JsonSerializer.Serialize(column);
        await bridge!.EvaluateAsync("import('../BaseComponents/Services/GridApiProvider.js').then(function (m) { window.__clvApi = m.default.getGridApi(); }); 0");
        await Task.Delay(400);
        var click = "(function () { var el = document.querySelector('#myGrid .ag-center-cols-container [row-id=' + JSON.stringify(" + rowId + ") + '] [col-id=' + JSON.stringify(" + columnId + ") + ']'); if (!el) { return 'cell not rendered'; } el.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, view: window })); el.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, view: window })); el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window })); el.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true, view: window })); return 'double-clicked'; })()";
        var clicked = JsonSerializer.Deserialize<string>(await bridge.EvaluateAsync(click));
        await Task.Delay(700);
        var type = "(function () { var input = document.querySelector('#myGrid .ag-cell-inline-editing input, #myGrid .ag-popup-editor input'); if (!input) { return 'no editor opened'; } Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, " + JsonSerializer.Serialize(typed) + "); input.dispatchEvent(new Event('input', { bubbles: true })); input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true })); return 'editor opened, typed, Enter'; })()";
        var typedResult = JsonSerializer.Deserialize<string>(await bridge.EvaluateAsync(type));
        await Task.Delay(2000);
        return clicked + "; " + typedResult;
    }

    private async Task PostCellEditAsync(int rowIndex, string column, string newValue)
    {
        var message = JsonSerializer.Serialize(new
        {
            EventName = "CellEditEvent",
            EventArgs = new { CellEditArgs = new[] { new { RowId = LevelsRecordsExtractor.ToRowId(rowIndex), ColumnName = column, CellValue = (string?)null, NewValue = newValue } } },
        });
        await bridge!.EvaluateAsync("window.chrome.webview.postMessage(" + JsonSerializer.Serialize(message) + ")");
        await Task.Delay(2000);
    }

    /// <summary>Edits one specification-held value and one uservar-held value in the per-selector view.</summary>
    private async Task<Dictionary<string, object?>> RunResolvedEditAsync(LevelNode node)
    {
        var result = new Dictionary<string, object?>();
        var tcgOnDisk = File.ReadAllText(service!.TestConditionsPath);
        var levelsOnDisk = File.ReadAllText(service.LevelsPath);
        var before = service.Snapshot;
        var table = LevelTableProjector.Project(FindLevel(before, node.Level)!);
        var group = FindGroup(table, node.Group)!;
        var finder = new ParameterResolver(userVars);
        var otherCondition = group.TestConditions.FirstOrDefault(candidate => candidate.Selector != node.Selector);

        (int Row, LevelTableCell Cell, ParameterEditTarget Target)? listEntry = null;
        (int Row, LevelTableCell Cell, ParameterEditTarget Target)? fromUserVar = null;
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            foreach (var cell in table.Rows[rowIndex].Cells)
            {
                if (cell.Value.Length == 0 || !ParameterResolver.IsParameterProperty(cell.Column)) continue;
                var target = finder.FindEditTarget(cell.Value, group, node.Selector!, before.TestConditionsText);
                if (listEntry == null && target.Editable && target.Kind == PairedDocumentKind.TestConditions && !target.AppliesToAllSelectors) listEntry = (rowIndex, cell, target);
                if (fromUserVar == null && !target.Editable && target.Reason!.Contains("uservar", StringComparison.Ordinal)) fromUserVar = (rowIndex, cell, target);
            }
        }

        if (fromUserVar != null)
        {
            result["userVarUi"] = await UiEditAsync(fromUserVar.Value.Row, fromUserVar.Value.Cell.Column, "9.99A");
            result["userVarCell"] = table.Rows[fromUserVar.Value.Row].Name + " / " + fromUserVar.Value.Cell.Column;
            result["userVarEditRejectedWith"] = StatusText.Text;
            result["userVarEditChangedText"] = service.Snapshot.TestConditionsText != tcgOnDisk || service.Snapshot.LevelsText != levelsOnDisk;
        }

        if (listEntry == null)
        {
            result["skipped"] = "no per-selector value to edit";
            return result;
        }

        var (row, editCell, editTarget) = listEntry.Value;
        var old = before.TestConditionsText.Substring(editTarget.Span.Start, editTarget.Span.Length);
        var unit = new string(old.Reverse().TakeWhile(char.IsLetter).Reverse().ToArray());
        var sent = "0.123" + unit;
        var otherBefore = otherCondition == null ? null : finder.Resolve(editCell.Value, group, otherCondition.Selector).Text;

        result["ui"] = await UiEditAsync(row, editCell.Column, sent);

        var after = service.Snapshot;
        var edited = after.TestConditionsText;
        var prefix = 0;
        while (prefix < edited.Length && prefix < tcgOnDisk.Length && edited[prefix] == tcgOnDisk[prefix]) prefix++;
        var suffix = 0;
        while (suffix < edited.Length - prefix && suffix < tcgOnDisk.Length - prefix && edited[edited.Length - 1 - suffix] == tcgOnDisk[tcgOnDisk.Length - 1 - suffix]) suffix++;
        var afterGroup = FindGroup(LevelTableProjector.Project(FindLevel(after, node.Level)!), node.Group)!;

        result["cell"] = table.Rows[row].Name + " / " + editCell.Column + " (" + editCell.Value + ")";
        result["owner"] = editTarget.Owner;
        result["old"] = old;
        result["sent"] = sent;
        result["shownInGrid"] = await ReadGridCellAsync(row, editCell.Column);
        result["status"] = StatusText.Text;
        result["tcgTextRemoved"] = tcgOnDisk.Substring(prefix, tcgOnDisk.Length - prefix - suffix);
        result["tcgTextInserted"] = edited.Substring(prefix, edited.Length - prefix - suffix);
        result["levelsTextChanged"] = after.LevelsText != levelsOnDisk;
        if (otherCondition != null)
        {
            result["otherSelector"] = otherCondition.Selector;
            result["otherSelectorBefore"] = otherBefore;
            result["otherSelectorAfter"] = new ParameterResolver(userVars).Resolve(editCell.Value, afterGroup, otherCondition.Selector).Text;
        }

        if (selfTestSave)
        {
            result["saved"] = TrySave();
            result["saveStatus"] = StatusText.Text;
        }
        else
        {
            service.ReloadFromDisk();
        }
        return result;
    }

    private Dictionary<string, object?> RunDuplicateCheck()
    {
        var result = new Dictionary<string, object?>();
        if (service == null) return result;
        service.ReloadFromDisk();
        FilterBox.Text = string.Empty;
        var snapshot = service.Snapshot;
        var stopwatch = Stopwatch.StartNew();
        var analysis = DuplicateAnalysis.Analyze(snapshot.LastValidModel, userVars);
        stopwatch.Stop();
        RefreshLevels(snapshot);

        var levelItems = LevelTree.Items.OfType<TreeViewItem>().ToList();
        result["analysisMilliseconds"] = stopwatch.ElapsedMilliseconds;
        result["highlightedLevelSets"] = levelItems.Count(item => item.Background == CompareWindow.DuplicateBrush);
        result["highlightedTestConditions"] = levelItems.SelectMany(item => item.Items.OfType<TreeViewItem>()).Count(item => item.Background == CompareWindow.DuplicateBrush);
        result["identicalGroups"] = snapshot.LastValidModel.LevelSets
            .Select(level => new[] { level.LevelSet.Name }.Concat(analysis.ForLevel(level.LevelSet.Name).Where(item => item.Relation == LevelDuplicateRelation.Identical).Select(item => item.OtherLevel)).OrderBy(name => name, StringComparer.Ordinal).ToList())
            .Where(group => group.Count > 1)
            .Select(group => string.Join(" = ", group))
            .Distinct()
            .ToList();
        result["statusTail"] = StatusText.Text.Substring(Math.Max(0, StatusText.Text.Length - 110));

        var node = new LevelNode("SBF_ARR", null, null, null);
        var item = levelItems.FirstOrDefault(candidate => (candidate.Tag as LevelNode)?.Key == node.Key);
        result["sbfArrToolTip"] = item?.ToolTip?.ToString()?.Replace("\n", "  |  ");
        result["sbfArrMenu"] = (item?.ContextMenu?.Items[0] as MenuItem)?.Header?.ToString();
        var comparisons = BuildComparisons(node, snapshot);
        result["sbfArrComparisons"] = comparisons.Select(comparison => comparison.Title).ToList();
        result["definedDifferentlyByComparison"] = comparisons.Select(comparison =>
        {
            var compared = comparison.Build();
            return comparison.RightName + ": " + compared.Count(row => row.Defined == CompareRow.Differently) + " of " + compared.Count + " defined differently, " + compared.Count(row => row.Defined == CompareRow.ValuesDiffer) + " values differ";
        }).ToList();
        var withSbf = comparisons.FirstOrDefault(comparison => comparison.RightName == "SBF");
        if (withSbf != null)
        {
            var rows = withSbf.Build();
            result["sbfArrVsSbfRows"] = rows.Count;
            result["sbfArrVsSbfDefinedDifferently"] = rows.Count(row => row.Defined == CompareRow.Differently);
            result["sbfArrVsSbfValuesDiffer"] = rows.Count(row => row.Defined == CompareRow.ValuesDiffer);
            var example = rows.FirstOrDefault(row => row.Defined == CompareRow.Differently);
            if (example != null) result["exampleDifferentDefinition"] = example.Selector + " " + example.Resource + "/" + example.Property + ": " + example.LeftHow + "   VS   " + example.RightHow;
        }

        // Make sure the compare window can be built and shown.
        var window = new CompareWindow(this, node.Level, comparisons);
        window.Show();
        window.Close();
        result["compareWindowOpened"] = true;
        return result;
    }

    /// <summary>
    /// Edits a cell the way a person does: double-click the cell, type into the editor, press Enter.
    /// Reports each stage so a failure shows where real editing stops.
    /// </summary>
    private async Task<Dictionary<string, object?>> RunUiEditAsync()
    {
        var result = new Dictionary<string, object?>();
        var linked = service == null ? null : FindLevel(service.Snapshot, displayedNode?.Level);
        if (service == null || bridge == null || linked == null) return result;

        var table = LevelTableProjector.Project(linked);
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var cell = table.Rows[rowIndex].Cells.FirstOrDefault(candidate => candidate.EditableSpan.HasValue);
            if (cell == null) continue;
            var rowId = JsonSerializer.Serialize(LevelsRecordsExtractor.ToRowId(rowIndex));
            var column = JsonSerializer.Serialize(cell.Column);
            const string typed = "0.456";

            await bridge.EvaluateAsync("import('../BaseComponents/Services/GridApiProvider.js').then(function (m) { window.__clvApi = m.default.getGridApi(); }); 0");
            await Task.Delay(500);

            var start = "(function () { var out = {}; try { var api = window.__clvApi; out.hasApi = !!api; var node = api.getRowNode(" + rowId + "); out.hasNode = !!node; var col = api.getColumn(" + column + "); out.hasColumn = !!col; out.columnEditable = col ? col.isCellEditable(node) : null; out.readOnlyEdit = api.getGridOption('readOnlyEdit'); var el = document.querySelector('#myGrid .ag-center-cols-container [row-id=' + JSON.stringify(" + rowId + ") + '] [col-id=' + JSON.stringify(" + column + ") + ']'); out.cellFound = !!el; if (el) { el.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, view: window })); el.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, view: window })); el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window })); el.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true, view: window })); } } catch (e) { out.error = String(e); } return JSON.stringify(out); })()";
            result["start"] = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Deserialize<string>(await bridge.EvaluateAsync(start)) ?? "{}");
            await Task.Delay(700);

            var type = "(function () { var out = {}; try { var api = window.__clvApi; out.editingCells = api.getEditingCells().length; var input = document.querySelector('#myGrid .ag-cell-inline-editing input, #myGrid .ag-popup-editor input'); out.editorFound = !!input; if (input) { out.editorInitialValue = input.value; Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, " + JsonSerializer.Serialize(typed) + "); input.dispatchEvent(new Event('input', { bubbles: true })); input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true })); } } catch (e) { out.error = String(e); } return JSON.stringify(out); })()";
            result["type"] = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Deserialize<string>(await bridge.EvaluateAsync(type)) ?? "{}");
            await Task.Delay(1500);

            result["cell"] = table.Rows[rowIndex].Name + " / " + cell.Column;
            result["before"] = cell.Value;
            result["typed"] = typed;
            result["shownInGridAfter"] = await ReadGridCellAsync(rowIndex, cell.Column);
            result["modelDirty"] = service.Snapshot.IsDirty;
            result["status"] = StatusText.Text;
            service.ReloadFromDisk();
            await Task.Delay(800);
            return result;
        }
        return result;
    }

    /// <summary>
    /// Types into a cell and leaves the editor open (no Enter, no click elsewhere), then does what the Save
    /// button does. In a normal self-test the value is only committed; with --selftest-save it is saved.
    /// </summary>
    private async Task<Dictionary<string, object?>> RunOpenEditorAsync()
    {
        var result = new Dictionary<string, object?>();
        var linked = service == null ? null : FindLevel(service.Snapshot, displayedNode?.Level);
        if (service == null || bridge == null || linked == null) return result;

        var table = LevelTableProjector.Project(linked);
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var cell = table.Rows[rowIndex].Cells.FirstOrDefault(candidate => candidate.EditableSpan.HasValue);
            if (cell == null) continue;
            const string typed = "0.789";
            var levelsOnDisk = File.ReadAllText(service.LevelsPath);

            await ReadGridCellAsync(rowIndex, cell.Column);
            var rowId = JsonSerializer.Serialize(LevelsRecordsExtractor.ToRowId(rowIndex));
            var columnId = JsonSerializer.Serialize(cell.Column);
            var open = "(function () { var el = document.querySelector('#myGrid .ag-center-cols-container [row-id=' + JSON.stringify(" + rowId + ") + '] [col-id=' + JSON.stringify(" + columnId + ") + ']'); if (!el) { return 'cell not rendered'; } el.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true, view: window })); return 'double-clicked'; })()";
            result["open"] = JsonSerializer.Deserialize<string>(await bridge.EvaluateAsync(open));
            await Task.Delay(700);
            var type = "(function () { var input = document.querySelector('#myGrid .ag-cell-inline-editing input, #myGrid .ag-popup-editor input'); if (!input) { return 'no editor opened'; } Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, " + JsonSerializer.Serialize(typed) + "); input.dispatchEvent(new Event('input', { bubbles: true })); return 'typed, editor left open'; })()";
            result["type"] = JsonSerializer.Deserialize<string>(await bridge.EvaluateAsync(type));
            await Task.Delay(500);
            result["appliedWhileEditorOpen"] = service.Snapshot.IsDirty;

            if (selfTestSave)
            {
                result["saved"] = await SaveAsync();
                var saved = File.ReadAllText(service.LevelsPath);
                result["fileChangedOnDisk"] = saved != levelsOnDisk;
                result["fileHasTypedValue"] = saved.Contains(cell.Column + " = " + typed + ";", StringComparison.Ordinal);
                result["fileLengthChange"] = saved.Length - levelsOnDisk.Length;
            }
            else
            {
                result["committed"] = await CommitPendingEditAsync();
            }

            await Task.Delay(1000);
            result["cell"] = table.Rows[rowIndex].Name + " / " + cell.Column;
            result["before"] = cell.Value;
            result["typed"] = typed;
            result["appliedAfter"] = FindLevel(service.Snapshot, linked.LevelSet.Name) is { } after
                && LevelTableProjector.Project(after).Rows[rowIndex].Cells.First(candidate => candidate.Column == cell.Column).Value == typed;
            result["shownInGridAfter"] = await ReadGridCellAsync(rowIndex, cell.Column);
            result["status"] = StatusText.Text.Substring(0, Math.Min(90, StatusText.Text.Length));
            if (!selfTestSave)
            {
                service.ReloadFromDisk();
                await Task.Delay(800);
            }
            return result;
        }
        return result;
    }

    private async Task<Dictionary<string, object?>> RunEditRoundTripAsync()
    {
        var result = new Dictionary<string, object?>();
        var linked = service == null ? null : FindLevel(service.Snapshot, displayedNode?.Level);
        if (service == null || bridge == null || linked == null)
        {
            result["skipped"] = "no level displayed";
            return result;
        }

        var table = LevelTableProjector.Project(linked);
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var cell = table.Rows[rowIndex].Cells.FirstOrDefault(candidate => candidate.EditableSpan.HasValue);
            if (cell == null) continue;

            const string sent = "0.123";
            var message = JsonSerializer.Serialize(new
            {
                EventName = "CellEditEvent",
                EventArgs = new { CellEditArgs = new[] { new { RowId = LevelsRecordsExtractor.ToRowId(rowIndex), ColumnName = cell.Column, CellValue = cell.Value, NewValue = sent } } },
            });
            await bridge.EvaluateAsync("window.chrome.webview.postMessage(" + JsonSerializer.Serialize(message) + ")");
            await Task.Delay(1500);

            result["row"] = table.Rows[rowIndex].Name;
            result["column"] = cell.Column;
            result["before"] = cell.Value;
            result["sent"] = sent;
            result["shownInGrid"] = await ReadGridCellAsync(rowIndex, cell.Column);
            result["modelDirty"] = service.Snapshot.IsDirty;
            return result;
        }

        result["skipped"] = "no editable cell in the first level";
        return result;
    }

    private async Task<Dictionary<string, object?>> RunClampCheckAsync()
    {
        var result = new Dictionary<string, object?>();
        if (service == null || bridge == null) return result;

        service.ReloadFromDisk(); // Drop the unsaved self-test edit before auditing.
        await Task.Delay(500);
        var model = service.Snapshot.LastValidModel;
        var audit = ClampResolver.Audit(model, userVars);
        result["userVarDefinitions"] = userVars.Definitions.Count;
        result["userVarsProblem"] = userVarsProblem;
        result["clampReferences"] = audit.References;
        result["unresolved"] = audit.Unresolved;
        result["firstProblem"] = audit.FirstProblem;

        // Show the first level that references a clamp parameter and read the derived cell back from the grid.
        foreach (var level in model.LevelSets)
        {
            var table = LevelTableProjector.Project(level);
            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                foreach (var cell in table.Rows[rowIndex].Cells)
                {
                    if (!ClampResolver.IsClampProperty(cell.Column)) continue;
                    var resolution = ClampResolver.Resolve(cell.Value, table.Groups, userVars);
                    if (resolution == null) continue;

                    FilterBox.Text = string.Empty;
                    SelectNode(level.LevelSet.Name);
                    await Task.Delay(2000);
                    result["sampleLevel"] = level.LevelSet.Name;
                    result["sampleColumn"] = cell.Column;
                    result["sampleLevelValue"] = cell.Value;
                    result["sampleExpected"] = resolution.Summary;
                    result["sampleShownInGrid"] = await ReadGridCellAsync(rowIndex, cell.Column + LevelsRecordsExtractor.UserVarColumnSuffix);
                    return result;
                }
            }
        }

        return result;
    }

    private async Task<Dictionary<string, object?>> RunParameterCheckAsync()
    {
        var result = new Dictionary<string, object?>();
        if (service == null || bridge == null) return result;

        var model = service.Snapshot.LastValidModel;
        var stopwatch = Stopwatch.StartNew();
        var audit = ParameterResolver.Audit(model, userVars);
        stopwatch.Stop();
        result["auditMilliseconds"] = stopwatch.ElapsedMilliseconds;
        result["references"] = audit.References;
        result["unresolved"] = audit.Unresolved;
        result["distinctProblems"] = audit.Problems.Count;
        result["problems"] = audit.Problems.Take(12).ToList();

        // The SBF level set must drop down into its test conditions; open SBF_nom_lvl and read a resolved cell.
        var sbf = FindLevel(service.Snapshot, "SBF");
        if (sbf == null)
        {
            result["sbf"] = "no level set named SBF";
            return result;
        }

        var sbfItem = LevelTree.Items.OfType<TreeViewItem>().FirstOrDefault(item => (item.Tag as LevelNode)?.Key == "SBF");
        result["sbfChildren"] = sbfItem?.Items.OfType<TreeViewItem>().Select(item => item.Header?.ToString()).ToList();

        var table = LevelTableProjector.Project(sbf);
        foreach (var group in table.Groups)
        {
            var condition = group.TestConditions.FirstOrDefault(candidate => candidate.Name == "SBF_nom_lvl") ?? group.TestConditions.FirstOrDefault();
            if (condition == null) continue;
            var node = new LevelNode("SBF", group.Group.Name, condition.Name, condition.Selector);
            result["selected"] = SelectNode(node.Key);
            await Task.Delay(2500);
            result["title"] = LevelTitle.Text;

            var samples = new List<Dictionary<string, object?>>();
            var seenExpressions = new HashSet<string>(StringComparer.Ordinal);
            for (var rowIndex = 0; rowIndex < table.Rows.Count && samples.Count < 6; rowIndex++)
            {
                foreach (var cell in table.Rows[rowIndex].Cells)
                {
                    if (!ParameterResolver.IsParameterProperty(cell.Column)) continue;
                    var resolved = resolver.Resolve(cell.Value, group, condition.Selector);
                    if (!resolved.Changed || !seenExpressions.Add(cell.Value)) continue;
                    samples.Add(new Dictionary<string, object?>
                    {
                        ["row"] = table.Rows[rowIndex].Name,
                        ["column"] = cell.Column,
                        ["expression"] = cell.Value,
                        ["trace"] = resolved.Trace.Replace("\n", "  ;  "),
                        ["expected"] = resolved.Text,
                        ["shownInGrid"] = await ReadGridCellAsync(rowIndex, cell.Column),
                    });
                    if (samples.Count >= 6) break;
                }
            }
            result["samples"] = samples;
            result["resolvedEdit"] = await RunResolvedEditAsync(node);
            return result;
        }

        result["sbf"] = "SBF has no test condition";
        return result;
    }
}
