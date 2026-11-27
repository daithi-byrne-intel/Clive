using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace CombinedLevelsViewer.Core;

public enum PairedDocumentKind
{
    Levels,
    TestConditions,
}

public sealed class PairedDocumentSnapshot
{
    public PairedDocumentSnapshot(
        string levelsText,
        string testConditionsText,
        CombinedLevelsModel lastValidModel,
        IReadOnlyList<DocumentDiagnostic> currentDiagnostics,
        bool levelsDirty,
        bool testConditionsDirty,
        long revision)
    {
        LevelsText = levelsText;
        TestConditionsText = testConditionsText;
        LastValidModel = lastValidModel;
        CurrentDiagnostics = currentDiagnostics;
        LevelsDirty = levelsDirty;
        TestConditionsDirty = testConditionsDirty;
        Revision = revision;
    }

    public string LevelsText { get; }
    public string TestConditionsText { get; }
    public CombinedLevelsModel LastValidModel { get; }
    public IReadOnlyList<DocumentDiagnostic> CurrentDiagnostics { get; }
    public bool LevelsDirty { get; }
    public bool TestConditionsDirty { get; }
    public bool IsDirty => LevelsDirty || TestConditionsDirty;
    public long Revision { get; }
}

public sealed class SnapshotChangedEventArgs : EventArgs
{
    public SnapshotChangedEventArgs(PairedDocumentSnapshot snapshot) => Snapshot = snapshot;
    public PairedDocumentSnapshot Snapshot { get; }
}

public sealed class ExternalChangeEventArgs : EventArgs
{
    public ExternalChangeEventArgs(PairedDocumentKind kind, string path)
    {
        Kind = kind;
        Path = path;
    }

    public PairedDocumentKind Kind { get; }
    public string Path { get; }
}

public sealed class PairedDocumentService : IDisposable
{
    private readonly object gate = new object();
    private readonly FileSystemWatcher levelsWatcher;
    private readonly FileSystemWatcher testConditionsWatcher;
    private PairedDocumentSnapshot snapshot;
    private FileFingerprint levelsFingerprint;
    private FileFingerprint testConditionsFingerprint;
    private int disposed;

    public PairedDocumentService(string levelsPath, string testConditionsPath)
    {
        LevelsPath = Path.GetFullPath(levelsPath ?? throw new ArgumentNullException(nameof(levelsPath)));
        TestConditionsPath = Path.GetFullPath(testConditionsPath ?? throw new ArgumentNullException(nameof(testConditionsPath)));

        var levelsText = File.ReadAllText(LevelsPath);
        var testConditionsText = File.ReadAllText(TestConditionsPath);
        var model = ParseAndLink(levelsText, testConditionsText);
        snapshot = new PairedDocumentSnapshot(levelsText, testConditionsText, model, model.Diagnostics, false, false, 0);
        levelsFingerprint = FileFingerprint.Read(LevelsPath);
        testConditionsFingerprint = FileFingerprint.Read(TestConditionsPath);
        levelsWatcher = CreateWatcher(LevelsPath, PairedDocumentKind.Levels);
        testConditionsWatcher = CreateWatcher(TestConditionsPath, PairedDocumentKind.TestConditions);
    }

    public string LevelsPath { get; }
    public string TestConditionsPath { get; }

    public PairedDocumentSnapshot Snapshot
    {
        get
        {
            lock (gate) return snapshot;
        }
    }

    public event EventHandler<SnapshotChangedEventArgs>? SnapshotChanged;
    public event EventHandler<ExternalChangeEventArgs>? ExternalChangeDetected;

    public bool UpdateText(PairedDocumentKind kind, string text)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        ThrowIfDisposed();
        PairedDocumentSnapshot updated;
        lock (gate)
        {
            var levelsText = kind == PairedDocumentKind.Levels ? text : snapshot.LevelsText;
            var testConditionsText = kind == PairedDocumentKind.TestConditions ? text : snapshot.TestConditionsText;
            updated = CreateUpdatedSnapshot(levelsText, testConditionsText, kind == PairedDocumentKind.Levels || snapshot.LevelsDirty, kind == PairedDocumentKind.TestConditions || snapshot.TestConditionsDirty);
            snapshot = updated;
        }
        SnapshotChanged?.Invoke(this, new SnapshotChangedEventArgs(updated));
        return updated.CurrentDiagnostics.Count == 0;
    }

    public bool ApplyExpressionEdit(PairedDocumentKind kind, TextSpan span, string expression)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));
        if (expression.IndexOf(';') >= 0 || expression.IndexOfAny(new[] { '\r', '\n' }) >= 0) return false;

        var current = Snapshot;
        var text = kind == PairedDocumentKind.Levels ? current.LevelsText : current.TestConditionsText;
        if (span.Start < 0 || span.End > text.Length) return false;
        var updated = text.Substring(0, span.Start) + expression + text.Substring(span.End);
        UpdateText(kind, updated);
        return true;
    }

    public void ReloadFromDisk()
    {
        ThrowIfDisposed();
        var levelsText = File.ReadAllText(LevelsPath);
        var testConditionsText = File.ReadAllText(TestConditionsPath);
        var model = ParseAndLink(levelsText, testConditionsText);
        PairedDocumentSnapshot updated;
        lock (gate)
        {
            levelsFingerprint = FileFingerprint.Read(LevelsPath);
            testConditionsFingerprint = FileFingerprint.Read(TestConditionsPath);
            updated = new PairedDocumentSnapshot(levelsText, testConditionsText, model, model.Diagnostics, false, false, snapshot.Revision + 1);
            snapshot = updated;
        }
        SnapshotChanged?.Invoke(this, new SnapshotChangedEventArgs(updated));
    }

    public void Save()
    {
        ThrowIfDisposed();
        PairedDocumentSnapshot current;
        lock (gate) current = snapshot;

        foreach (var diagnostic in current.CurrentDiagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                throw new InvalidOperationException("Cannot save while either source file has syntax errors.");
            }
        }

        if (HasExternalChanges(PairedDocumentKind.Levels) || HasExternalChanges(PairedDocumentKind.TestConditions))
        {
            throw new PairedDocumentConflictException("One or both source files changed on disk. Reload or compare before saving.");
        }

        var transactionId = Guid.NewGuid().ToString("N");
        var levelsTemporary = LevelsPath + "." + transactionId + ".tmp";
        var testConditionsTemporary = TestConditionsPath + "." + transactionId + ".tmp";
        var levelsBackup = LevelsPath + ".combinedlevels.bak";
        var testConditionsBackup = TestConditionsPath + ".combinedlevels.bak";
        var journal = Path.Combine(Path.GetDirectoryName(LevelsPath) ?? ".", ".combinedlevels-save-journal");
        var levelsReplaced = false;

        try
        {
            WriteUtf8(levelsTemporary, current.LevelsText);
            WriteUtf8(testConditionsTemporary, current.TestConditionsText);
            File.WriteAllText(journal, string.Join(Environment.NewLine, transactionId, LevelsPath, levelsTemporary, levelsBackup, TestConditionsPath, testConditionsTemporary, testConditionsBackup));
            ReplaceWithBackup(levelsTemporary, LevelsPath, levelsBackup);
            levelsReplaced = true;
            ReplaceWithBackup(testConditionsTemporary, TestConditionsPath, testConditionsBackup);
            AcceptSavedState(FileFingerprint.Read(LevelsPath), FileFingerprint.Read(TestConditionsPath));
            File.Delete(journal);
        }
        catch
        {
            if (levelsReplaced && File.Exists(levelsBackup)) File.Copy(levelsBackup, LevelsPath, true);
            if (File.Exists(testConditionsBackup) && !File.Exists(TestConditionsPath)) File.Copy(testConditionsBackup, TestConditionsPath, true);
            throw;
        }
        finally
        {
            TryDelete(levelsTemporary);
            TryDelete(testConditionsTemporary);
        }
    }

    internal bool HasExternalChanges(PairedDocumentKind kind)
    {
        lock (gate)
        {
            return kind == PairedDocumentKind.Levels
                ? !levelsFingerprint.Equals(FileFingerprint.Read(LevelsPath))
                : !testConditionsFingerprint.Equals(FileFingerprint.Read(TestConditionsPath));
        }
    }

    internal void AcceptSavedState(FileFingerprint levels, FileFingerprint testConditions)
    {
        PairedDocumentSnapshot updated;
        lock (gate)
        {
            levelsFingerprint = levels;
            testConditionsFingerprint = testConditions;
            updated = new PairedDocumentSnapshot(snapshot.LevelsText, snapshot.TestConditionsText, snapshot.LastValidModel, snapshot.CurrentDiagnostics, false, false, snapshot.Revision + 1);
            snapshot = updated;
        }
        SnapshotChanged?.Invoke(this, new SnapshotChangedEventArgs(updated));
    }

    private PairedDocumentSnapshot CreateUpdatedSnapshot(string levelsText, string testConditionsText, bool levelsDirty, bool testConditionsDirty)
    {
        var levels = LevelsParser.Parse(levelsText);
        var testConditions = TcgParser.Parse(testConditionsText);
        var diagnostics = new List<DocumentDiagnostic>();
        AddRange(diagnostics, levels.Diagnostics);
        AddRange(diagnostics, testConditions.Diagnostics);
        var lastValid = snapshot.LastValidModel;
        if (!levels.HasErrors && !testConditions.HasErrors)
        {
            lastValid = SemanticLinker.Link(levels, testConditions);
            diagnostics = new List<DocumentDiagnostic>(lastValid.Diagnostics);
        }
        return new PairedDocumentSnapshot(levelsText, testConditionsText, lastValid, diagnostics, levelsDirty, testConditionsDirty, snapshot.Revision + 1);
    }

    private FileSystemWatcher CreateWatcher(string path, PairedDocumentKind kind)
    {
        var watcher = new FileSystemWatcher(Path.GetDirectoryName(path) ?? ".", Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler changed = (_, __) => OnFileChanged(kind, path);
        RenamedEventHandler renamed = (_, __) => OnFileChanged(kind, path);
        watcher.Changed += changed;
        watcher.Created += changed;
        watcher.Deleted += changed;
        watcher.Renamed += renamed;
        return watcher;
    }

    private void OnFileChanged(PairedDocumentKind kind, string path)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        if (HasExternalChanges(kind)) ExternalChangeDetected?.Invoke(this, new ExternalChangeEventArgs(kind, path));
    }

    private static CombinedLevelsModel ParseAndLink(string levelsText, string testConditionsText)
        => SemanticLinker.Link(LevelsParser.Parse(levelsText), TcgParser.Parse(testConditionsText));

    private static void AddRange<T>(List<T> destination, IReadOnlyList<T> source)
    {
        foreach (var item in source) destination.Add(item);
    }

    private static void ReplaceWithBackup(string temporaryPath, string targetPath, string backupPath)
    {
        TryDelete(backupPath);
        File.Replace(temporaryPath, targetPath, backupPath, true);
    }

    private static void WriteUtf8(string path, string text)
    {
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(PairedDocumentService));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        levelsWatcher.Dispose();
        testConditionsWatcher.Dispose();
    }
}

public sealed class PairedDocumentConflictException : IOException
{
    public PairedDocumentConflictException(string message)
        : base(message)
    {
    }
}

internal readonly struct FileFingerprint : IEquatable<FileFingerprint>
{
    public FileFingerprint(long length, DateTime lastWriteUtc)
    {
        Length = length;
        LastWriteUtc = lastWriteUtc;
    }

    public long Length { get; }
    public DateTime LastWriteUtc { get; }

    public static FileFingerprint Read(string path)
    {
        var information = new FileInfo(path);
        return information.Exists ? new FileFingerprint(information.Length, information.LastWriteTimeUtc) : new FileFingerprint(-1, DateTime.MinValue);
    }

    public bool Equals(FileFingerprint other) => Length == other.Length && LastWriteUtc == other.LastWriteUtc;
    public override bool Equals(object? obj) => obj is FileFingerprint other && Equals(other);
    public override int GetHashCode() => Length.GetHashCode() ^ LastWriteUtc.GetHashCode();
}
