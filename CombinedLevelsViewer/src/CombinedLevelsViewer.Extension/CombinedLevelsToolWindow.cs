using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.Extensibility.UI;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace CombinedLevelsViewer.Extension;

[VisualStudioContribution]
internal sealed class CombinedLevelsToolWindow : ToolWindow
{
    public CombinedLevelsToolWindow(VisualStudioExtensibility extensibility)
        : base(extensibility)
    {
        Title = "Combined Levels Viewer";
    }

    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.DocumentWell,
        DockDirection = Dock.Right,
        AllowAutoCreation = false,
    };

    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IRemoteUserControl>(new CombinedLevelsControl(new CombinedLevelsViewModel()));
    }
}

internal sealed class CombinedLevelsControl : RemoteUserControl
{
    public CombinedLevelsControl(CombinedLevelsViewModel dataContext)
        : base(dataContext)
    {
    }

    public override Task<string> GetXamlAsync(CancellationToken cancellationToken)
    {
        const string xaml = """
<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Grid Margin="8">
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="240" />
      <ColumnDefinition Width="8" />
      <ColumnDefinition Width="*" />
      <ColumnDefinition Width="8" />
      <ColumnDefinition Width="280" />
    </Grid.ColumnDefinitions>
    <Grid Grid.Column="0">
      <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
      </Grid.RowDefinitions>
      <TextBox Text="{Binding SearchText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="Filter level sets" Margin="0,0,0,6" />
      <ListBox Grid.Row="1" ItemsSource="{Binding FilteredLevelSets}" SelectedItem="{Binding SelectedLevel, Mode=TwoWay}" DisplayMemberPath="Name" />
    </Grid>
    <GridSplitter Grid.Column="1" Width="6" HorizontalAlignment="Stretch" />
    <Grid Grid.Column="2">
      <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
        <RowDefinition Height="Auto" />
      </Grid.RowDefinitions>
      <TextBlock Text="{Binding SelectedLevel.Name}" FontSize="15" FontWeight="SemiBold" Margin="0,0,0,6" />
      <ScrollViewer Grid.Row="1" HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled">
        <ItemsControl ItemsSource="{Binding Columns}">
          <ItemsControl.ItemsPanel><ItemsPanelTemplate><StackPanel Orientation="Horizontal" /></ItemsPanelTemplate></ItemsControl.ItemsPanel>
          <ItemsControl.ItemTemplate><DataTemplate><Border BorderBrush="#555" BorderThickness="0,0,1,1" Padding="5" Width="150"><TextBlock Text="{Binding}" FontWeight="SemiBold" /></Border></DataTemplate></ItemsControl.ItemTemplate>
        </ItemsControl>
      </ScrollViewer>
      <ListBox Grid.Row="2" ItemsSource="{Binding Rows}" SelectedItem="{Binding SelectedRow, Mode=TwoWay}" HorizontalContentAlignment="Stretch">
        <ListBox.ItemTemplate><DataTemplate><StackPanel Orientation="Horizontal"><Border BorderBrush="#555" BorderThickness="0,0,1,1" Padding="5" Width="190"><TextBlock Text="{Binding Name}" /></Border><ItemsControl ItemsSource="{Binding Cells}"><ItemsControl.ItemsPanel><ItemsPanelTemplate><StackPanel Orientation="Horizontal" /></ItemsPanelTemplate></ItemsControl.ItemsPanel><ItemsControl.ItemTemplate><DataTemplate><Border BorderBrush="#555" BorderThickness="0,0,1,1" Padding="2" Width="150"><TextBox Text="{Binding Value, Mode=TwoWay, UpdateSourceTrigger=LostFocus}" IsReadOnly="{Binding IsReadOnly}" BorderThickness="0" /></Border></DataTemplate></ItemsControl.ItemTemplate></ItemsControl></StackPanel></DataTemplate></ListBox.ItemTemplate>
      </ListBox>
      <TextBlock Grid.Row="3" Text="{Binding Status}" Margin="0,6,0,0" TextWrapping="Wrap" />
    </Grid>
    <GridSplitter Grid.Column="3" Width="6" HorizontalAlignment="Stretch" />
    <Grid Grid.Column="4">
      <Grid.RowDefinitions><RowDefinition Height="Auto" /><RowDefinition Height="*" /></Grid.RowDefinitions>
      <TextBlock Text="Test-condition context" FontWeight="SemiBold" Margin="0,0,0,6" />
      <ListBox Grid.Row="1" ItemsSource="{Binding ContextItems}"><ListBox.ItemTemplate><DataTemplate><Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*" /><ColumnDefinition Width="120" /></Grid.ColumnDefinitions><TextBlock Text="{Binding Label}" TextWrapping="Wrap" /><TextBox Grid.Column="1" Text="{Binding Value, Mode=TwoWay, UpdateSourceTrigger=LostFocus}" IsReadOnly="{Binding IsReadOnly}" /></Grid></DataTemplate></ListBox.ItemTemplate></ListBox>
    </Grid>
  </Grid>
</DataTemplate>
""";
        return Task.FromResult(xaml);
    }
}
