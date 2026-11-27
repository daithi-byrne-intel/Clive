using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CombinedLevelsViewer.TorchTabular;

/// <summary>One compared cell: the value each side produces and how each side arrives at it.</summary>
internal sealed record CompareRow(string Selector, string Resource, string Property, string LeftValue, string RightValue, string LeftHow, string RightHow, string Defined)
{
    public const string SameWay = "same way";
    public const string Differently = "differently";
    public const string ValuesDiffer = "VALUES DIFFER";
}

/// <summary>A duplicate that the selected tree node can be compared with.</summary>
internal sealed record Comparison(string Title, string LeftName, string RightName, Func<List<CompareRow>> Build);

/// <summary>
/// Side-by-side comparison of a level set or test condition with one of its duplicates. The values are the
/// same by definition of a duplicate; the window shows where the two sides are defined differently.
/// </summary>
internal sealed class CompareWindow : Window
{
    public static readonly Brush DuplicateBrush = CreateDuplicateBrush();

    private readonly ComboBox comparisonBox;
    private readonly CheckBox differencesOnly;
    private readonly TextBlock summary;
    private readonly DataGrid grid;

    public CompareWindow(Window owner, string subject, IReadOnlyList<Comparison> comparisons)
    {
        Owner = owner;
        Title = "Compare " + subject + " with duplicates";
        Width = 1250;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        comparisonBox = new ComboBox { ItemsSource = comparisons, DisplayMemberPath = nameof(Comparison.Title), MinWidth = 420, Margin = new Thickness(8, 0, 16, 0) };
        differencesOnly = new CheckBox { Content = "Only show values that are defined differently", VerticalAlignment = VerticalAlignment.Center };
        summary = new TextBlock { Margin = new Thickness(8, 0, 8, 8), TextWrapping = TextWrapping.Wrap };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        header.Children.Add(new TextBlock { Text = "Compare " + subject + " with:", VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(comparisonBox);
        header.Children.Add(differencesOnly);

        var rowStyle = new Style(typeof(DataGridRow));
        var differently = new DataTrigger { Binding = new Binding(nameof(CompareRow.Defined)), Value = CompareRow.Differently };
        differently.Setters.Add(new Setter(BackgroundProperty, DuplicateBrush));
        rowStyle.Triggers.Add(differently);
        var valuesDiffer = new DataTrigger { Binding = new Binding(nameof(CompareRow.Defined)), Value = CompareRow.ValuesDiffer };
        valuesDiffer.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xFF, 0xCD, 0xD2))));
        rowStyle.Triggers.Add(valuesDiffer);

        grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowStyle = rowStyle,
            Margin = new Thickness(8, 0, 8, 8),
        };

        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(summary, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(summary);
        layout.Children.Add(grid);
        Content = layout;

        comparisonBox.SelectionChanged += (_, _) => ShowComparison();
        differencesOnly.Checked += (_, _) => ShowComparison();
        differencesOnly.Unchecked += (_, _) => ShowComparison();
        if (comparisons.Count > 0) comparisonBox.SelectedIndex = 0;
    }

    private void ShowComparison()
    {
        if (comparisonBox.SelectedItem is not Comparison comparison) return;
        var rows = comparison.Build();
        var differentlyDefined = rows.Count(row => row.Defined == CompareRow.Differently);
        var differentValues = rows.Count(row => row.Defined == CompareRow.ValuesDiffer);

        grid.Columns.Clear();
        AddColumn("Selector", nameof(CompareRow.Selector), 130);
        AddColumn("Resource / Break", nameof(CompareRow.Resource), 170);
        AddColumn("Property", nameof(CompareRow.Property), 100);
        AddColumn("Value in " + comparison.LeftName, nameof(CompareRow.LeftValue), 110);
        AddColumn("Value in " + comparison.RightName, nameof(CompareRow.RightValue), 110);
        AddColumn("Defined", nameof(CompareRow.Defined), 80);
        AddColumn("How " + comparison.LeftName + " gets it", nameof(CompareRow.LeftHow), 260);
        AddColumn("How " + comparison.RightName + " gets it", nameof(CompareRow.RightHow), 260);
        grid.ItemsSource = differencesOnly.IsChecked == true ? rows.Where(row => row.Defined != CompareRow.SameWay).ToList() : rows;

        summary.Text = rows.Count + " values compared. "
            + (differentValues > 0 ? differentValues + " differ (shown in red). " : "All values are equal. ")
            + (differentlyDefined == 0
                ? "Both sides define every value the same way."
                : differentlyDefined + " are defined differently (highlighted): they are equal now but would diverge if one definition changed.");
    }

    private void AddColumn(string header, string property, double width)
    {
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property), Width = width });
    }

    private static Brush CreateDuplicateBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0xF9, 0xC4)); // Light yellow.
        brush.Freeze();
        return brush;
    }
}
