using CombinedLevelsViewer.Core;
using System.Diagnostics;

namespace CombinedLevelsViewer.Core.Tests;

[TestClass]
public sealed class PairedDocumentServiceTests
{
    [TestMethod]
    public void ExpressionEditUpdatesTextAndLinkedSnapshot()
    {
        using var files = TemporaryPair.Create();
        using var service = new PairedDocumentService(files.LevelsPath, files.TestConditionsPath);
        var property = ((ResourceEntrySyntax)service.Snapshot.LastValidModel.LevelSets[0].LevelSet.Entries[0]).Properties[0];

        var applied = service.ApplyExpressionEdit(PairedDocumentKind.Levels, property.ExpressionSpan, "0.9V");

        Assert.IsTrue(applied);
        Assert.IsTrue(service.Snapshot.LevelsDirty);
        Assert.Contains("VForce = 0.9V;", service.Snapshot.LevelsText);
        var updatedProperty = ((ResourceEntrySyntax)service.Snapshot.LastValidModel.LevelSets[0].LevelSet.Entries[0]).Properties[0];
        Assert.AreEqual("0.9V", updatedProperty.Expression);
    }

    [TestMethod]
    public void InvalidSourceKeepsLastValidTableModel()
    {
        using var files = TemporaryPair.Create();
        using var service = new PairedDocumentService(files.LevelsPath, files.TestConditionsPath);
        var originalModel = service.Snapshot.LastValidModel;

        service.UpdateText(PairedDocumentKind.Levels, "Version 1.0; Levels BROKEN {");

        Assert.AreSame(originalModel, service.Snapshot.LastValidModel);
        Assert.IsTrue(service.Snapshot.CurrentDiagnostics.Any(item => item.Severity == DiagnosticSeverity.Error));
    }

    [TestMethod]
    public void SpecificationEditUpdatesOnlyTestConditionText()
    {
        using var files = TemporaryPair.Create();
        using var service = new PairedDocumentService(files.LevelsPath, files.TestConditionsPath);
        var originalLevels = service.Snapshot.LevelsText;
        var value = service.Snapshot.LastValidModel.TcgDocument.SpecificationSets[0].Values[0];

        var applied = service.ApplyExpressionEdit(PairedDocumentKind.TestConditions, value.ExpressionSpan, "0.9V");

        Assert.IsTrue(applied);
        Assert.AreEqual(originalLevels, service.Snapshot.LevelsText);
        Assert.IsTrue(service.Snapshot.TestConditionsDirty);
        Assert.Contains("Voltage p = 0.9V;", service.Snapshot.TestConditionsText);
        Assert.IsFalse(service.ApplyExpressionEdit(PairedDocumentKind.TestConditions, value.ExpressionSpan, "1V; injected = TRUE"));
    }

    [TestMethod]
    public void SaveWritesBothFilesAndRetainsRecoverableBackups()
    {
        using var files = TemporaryPair.Create();
        using var service = new PairedDocumentService(files.LevelsPath, files.TestConditionsPath);
        var property = ((ResourceEntrySyntax)service.Snapshot.LastValidModel.LevelSets[0].LevelSet.Entries[0]).Properties[0];
        var specification = service.Snapshot.LastValidModel.TcgDocument.SpecificationSets[0].Values[0];
        service.ApplyExpressionEdit(PairedDocumentKind.Levels, property.ExpressionSpan, "0.9V");
        service.ApplyExpressionEdit(PairedDocumentKind.TestConditions, specification.ExpressionSpan, "0.9V");

        service.Save();

        Assert.Contains("VForce = 0.9V", File.ReadAllText(files.LevelsPath));
        Assert.Contains("Voltage p = 0.9V", File.ReadAllText(files.TestConditionsPath));
        Assert.IsFalse(service.Snapshot.IsDirty);
        Assert.IsTrue(File.Exists(files.LevelsPath + ".combinedlevels.bak"));
        Assert.IsTrue(File.Exists(files.TestConditionsPath + ".combinedlevels.bak"));
    }

    [TestMethod]
    public void SaveRejectsExternalChanges()
    {
        using var files = TemporaryPair.Create();
        using var service = new PairedDocumentService(files.LevelsPath, files.TestConditionsPath);
        service.UpdateText(PairedDocumentKind.Levels, service.Snapshot.LevelsText.Replace("0.8V", "0.9V"));
        File.AppendAllText(files.TestConditionsPath, Environment.NewLine + "# external edit");

        Assert.Throws<PairedDocumentConflictException>(() => service.Save());
    }

    [TestMethod]
    public void ProductionSizedSourceUpdateRemainsInteractive()
    {
        var repositoryRoot = FindRepositoryRoot();
        var directory = Path.Combine(Path.GetTempPath(), "CombinedLevelsViewerTests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        var levelsPath = Path.Combine(directory, "LevelsSequences.lvl");
        var tcgPath = Path.Combine(directory, "BaseLevels.tcg");
        File.Copy(Path.Combine(repositoryRoot, "Shared", "Common", "LevelsSequences.lvl"), levelsPath);
        File.Copy(Path.Combine(repositoryRoot, "Shared", "Common", "BaseLevels.tcg"), tcgPath);
        try
        {
            using var service = new PairedDocumentService(levelsPath, tcgPath);
            var property = service.Snapshot.LastValidModel.LevelSets
                .SelectMany(level => level.LevelSet.Entries.OfType<ResourceEntrySyntax>())
                .SelectMany(resource => resource.Properties)
                .First();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();

            service.ApplyExpressionEdit(PairedDocumentKind.Levels, property.ExpressionSpan, property.Expression);

            stopwatch.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.IsLessThan(2000L, stopwatch.ElapsedMilliseconds, "A scalar edit and reparse must remain interactive.");
            Assert.IsLessThan(150L * 1024 * 1024, allocatedBytes, "A scalar edit must not allocate excessive memory.");
            TestContext.WriteLine($"Production scalar edit/reparse: {stopwatch.ElapsedMilliseconds} ms, {allocatedBytes / (1024d * 1024d):F2} MiB allocated");
        }
        finally
        {
            try { System.IO.Directory.Delete(directory, true); } catch { }
        }
    }

    public TestContext TestContext { get; set; } = null!;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NovaLake_Gdie_512EU.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private sealed class TemporaryPair : IDisposable
    {
        private TemporaryPair(string directory, string levelsPath, string testConditionsPath)
        {
            Directory = directory;
            LevelsPath = levelsPath;
            TestConditionsPath = testConditionsPath;
        }

        public string Directory { get; }
        public string LevelsPath { get; }
        public string TestConditionsPath { get; }

        public static TemporaryPair Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), "CombinedLevelsViewerTests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var levelsPath = Path.Combine(directory, "LevelsSequences.lvl");
            var tcgPath = Path.Combine(directory, "BaseLevels.tcg");
            File.WriteAllText(levelsPath, "Version 1.0;\nLevels L { pins { VForce = 0.8V; } }");
            File.WriteAllText(tcgPath, "Version 1.0;\nSpecificationSet S (nom) { Voltage p = 0.8V; }\nTestConditionGroup G { SpecificationSet = S; Level = __main__::L; }\nTestCondition T { TestConditionGroup = G; Selector = nom; }");
            return new TemporaryPair(directory, levelsPath, tcgPath);
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
