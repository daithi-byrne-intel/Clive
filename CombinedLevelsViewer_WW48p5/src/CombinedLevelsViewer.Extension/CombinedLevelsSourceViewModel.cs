using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using CombinedLevelsViewer.Core;
using Microsoft.VisualStudio.Extensibility.UI;

namespace CombinedLevelsViewer.Extension;

[DataContract]
internal sealed class CombinedLevelsSourceViewModel : NotifyPropertyChangedObject, IDisposable
{
    private readonly PairedDocumentService service;
    private CancellationTokenSource? levelsDebounce;
    private CancellationTokenSource? testConditionsDebounce;
    private string levelsText;
    private string testConditionsText;
    private string status = string.Empty;
    private int levelsCaretIndex;
    private int testConditionsCaretIndex;
    private bool applyingSnapshot;

    public CombinedLevelsSourceViewModel()
    {
        service = ViewerSession.GetOrCreateService();
        levelsText = service.Snapshot.LevelsText;
        testConditionsText = service.Snapshot.TestConditionsText;
        Diagnostics = new ObservableCollection<string>();
        service.SnapshotChanged += OnSnapshotChanged;
        service.ExternalChangeDetected += OnExternalChangeDetected;
        ViewerSession.SourceNavigationRequested += OnSourceNavigationRequested;
        SaveCommand = new AsyncCommand(SaveAsync);
        ReloadCommand = new AsyncCommand(ReloadAsync);
        ApplySnapshot(service.Snapshot);
    }

    [DataMember]
    public string LevelsText
    {
        get => levelsText;
        set
        {
            if (!SetProperty(ref levelsText, value ?? string.Empty) || applyingSnapshot) return;
            DebounceUpdate(PairedDocumentKind.Levels, levelsText);
        }
    }

    [DataMember]
    public string TestConditionsText
    {
        get => testConditionsText;
        set
        {
            if (!SetProperty(ref testConditionsText, value ?? string.Empty) || applyingSnapshot) return;
            DebounceUpdate(PairedDocumentKind.TestConditions, testConditionsText);
        }
    }

    [DataMember]
    public int LevelsCaretIndex
    {
        get => levelsCaretIndex;
        set
        {
            if (SetProperty(ref levelsCaretIndex, value)) ViewerSession.SelectLevelAt(PairedDocumentKind.Levels, value);
        }
    }

    [DataMember]
    public int TestConditionsCaretIndex
    {
        get => testConditionsCaretIndex;
        set => SetProperty(ref testConditionsCaretIndex, value);
    }

    [DataMember]
    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    [DataMember]
    public ObservableCollection<string> Diagnostics { get; }

    [DataMember]
    public IAsyncCommand SaveCommand { get; }

    [DataMember]
    public IAsyncCommand ReloadCommand { get; }

    private async Task SaveAsync(object? parameter, CancellationToken cancellationToken)
    {
        try
        {
            if (levelsDebounce != null) await levelsDebounce.CancelAsync();
            if (testConditionsDebounce != null) await testConditionsDebounce.CancelAsync();
            service.UpdateText(PairedDocumentKind.Levels, LevelsText);
            service.UpdateText(PairedDocumentKind.TestConditions, TestConditionsText);
            service.Save();
            Status = "Both files saved. Recoverable .combinedlevels.bak files were retained.";
        }
        catch (Exception exception)
        {
            Status = "Save failed: " + exception.Message;
        }
    }

    private async Task ReloadAsync(object? parameter, CancellationToken cancellationToken)
    {
        try
        {
            if (levelsDebounce != null) await levelsDebounce.CancelAsync();
            if (testConditionsDebounce != null) await testConditionsDebounce.CancelAsync();
            service.ReloadFromDisk();
            Status = "Both files reloaded from disk.";
        }
        catch (Exception exception)
        {
            Status = "Reload failed: " + exception.Message;
        }
    }

    private void DebounceUpdate(PairedDocumentKind kind, string text)
    {
        var source = new CancellationTokenSource();
        if (kind == PairedDocumentKind.Levels)
        {
            levelsDebounce?.Cancel();
            levelsDebounce?.Dispose();
            levelsDebounce = source;
        }
        else
        {
            testConditionsDebounce?.Cancel();
            testConditionsDebounce?.Dispose();
            testConditionsDebounce = source;
        }

        _ = UpdateAfterDelayAsync(kind, text, source);
    }

    private async Task UpdateAfterDelayAsync(PairedDocumentKind kind, string text, CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(350, source.Token).ConfigureAwait(false);
            service.UpdateText(kind, text);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Status = "Unable to update the paired document: " + exception.Message;
        }
    }

    private void OnSnapshotChanged(object? sender, SnapshotChangedEventArgs args) => ApplySnapshot(args.Snapshot);

    private void ApplySnapshot(PairedDocumentSnapshot snapshot)
    {
        applyingSnapshot = true;
        try
        {
            LevelsText = snapshot.LevelsText;
            TestConditionsText = snapshot.TestConditionsText;
        }
        finally
        {
            applyingSnapshot = false;
        }

        Diagnostics.Clear();
        var count = Math.Min(snapshot.CurrentDiagnostics.Count, 20);
        for (var index = 0; index < count; index++) Diagnostics.Add(snapshot.CurrentDiagnostics[index].Message);
        Status = snapshot.CurrentDiagnostics.Count == 0
            ? "Both files are valid" + (snapshot.IsDirty ? " — unsaved changes" : string.Empty)
            : snapshot.CurrentDiagnostics.Count + " diagnostic(s) — the table retains the last valid model";
    }

    private void OnExternalChangeDetected(object? sender, ExternalChangeEventArgs args)
    {
        Status = args.Kind + " changed on disk. Reload before saving to avoid overwriting external changes.";
    }

    private void OnSourceNavigationRequested(object? sender, SourceNavigationEventArgs args)
    {
        if (args.Kind == PairedDocumentKind.Levels)
        {
            LevelsCaretIndex = Math.Min(args.Span.Start, LevelsText.Length);
        }
        else
        {
            TestConditionsCaretIndex = Math.Min(args.Span.Start, TestConditionsText.Length);
        }
    }

    public void Dispose()
    {
        levelsDebounce?.Cancel();
        testConditionsDebounce?.Cancel();
        levelsDebounce?.Dispose();
        testConditionsDebounce?.Dispose();
        service.SnapshotChanged -= OnSnapshotChanged;
        service.ExternalChangeDetected -= OnExternalChangeDetected;
        ViewerSession.SourceNavigationRequested -= OnSourceNavigationRequested;
    }
}
