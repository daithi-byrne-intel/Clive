using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.Extensibility.UI;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace CombinedLevelsViewer.Extension;

[VisualStudioContribution]
internal sealed class CombinedLevelsSourceToolWindow : ToolWindow
{
    public CombinedLevelsSourceToolWindow(VisualStudioExtensibility extensibility)
        : base(extensibility)
    {
        Title = "Combined Levels Source";
    }

    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.DocumentWell,
        DockDirection = Dock.Right,
        AllowAutoCreation = false,
    };

    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IRemoteUserControl>(new CombinedLevelsSourceControl(new CombinedLevelsSourceViewModel()));
    }
}

internal sealed class CombinedLevelsSourceControl : RemoteUserControl
{
    public CombinedLevelsSourceControl(CombinedLevelsSourceViewModel dataContext)
        : base(dataContext)
    {
    }

    public override Task<string> GetXamlAsync(CancellationToken cancellationToken)
    {
        const string xaml = """
<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Grid Margin="8">
    <Grid.RowDefinitions><RowDefinition Height="Auto" /><RowDefinition Height="*" /><RowDefinition Height="Auto" /></Grid.RowDefinitions>
    <Grid>
      <Grid.ColumnDefinitions><ColumnDefinition Width="*" /><ColumnDefinition Width="8" /><ColumnDefinition Width="*" /></Grid.ColumnDefinitions>
      <TextBlock Text="LevelsSequences.lvl" FontWeight="SemiBold" />
      <TextBlock Grid.Column="2" Text="BaseLevels.tcg" FontWeight="SemiBold" />
    </Grid>
    <Grid Grid.Row="1" Margin="0,6,0,6">
      <Grid.ColumnDefinitions><ColumnDefinition Width="*" /><ColumnDefinition Width="8" /><ColumnDefinition Width="*" /></Grid.ColumnDefinitions>
      <TextBox Text="{Binding LevelsText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" CaretIndex="{Binding LevelsCaretIndex, Mode=TwoWay}" AcceptsReturn="True" AcceptsTab="True" FontFamily="Consolas" FontSize="12" HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto" />
      <GridSplitter Grid.Column="1" Width="6" HorizontalAlignment="Stretch" />
      <TextBox Grid.Column="2" Text="{Binding TestConditionsText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" CaretIndex="{Binding TestConditionsCaretIndex, Mode=TwoWay}" AcceptsReturn="True" AcceptsTab="True" FontFamily="Consolas" FontSize="12" HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto" />
    </Grid>
    <StackPanel Grid.Row="2">
      <StackPanel Orientation="Horizontal" Margin="0,0,0,4"><Button Content="Save both files" Command="{Binding SaveCommand}" Margin="0,0,6,0" /><Button Content="Reload from disk" Command="{Binding ReloadCommand}" /></StackPanel>
      <TextBlock Text="{Binding Status}" TextWrapping="Wrap" />
      <ItemsControl ItemsSource="{Binding Diagnostics}"><ItemsControl.ItemTemplate><DataTemplate><TextBlock Text="{Binding}" TextWrapping="Wrap" /></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>
    </StackPanel>
  </Grid>
</DataTemplate>
""";
        return Task.FromResult(xaml);
    }
}
