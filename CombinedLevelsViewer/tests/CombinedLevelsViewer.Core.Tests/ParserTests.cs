using System.Diagnostics;
using CombinedLevelsViewer.Core;

namespace CombinedLevelsViewer.Core.Tests;

[TestClass]
public sealed class ParserTests
{
    [TestMethod]
    public void PairedFixtureParsesAndLinksWithoutErrors()
    {
        var levelsText = ReadFixture("sample.lvl");
        var tcgText = ReadFixture("sample.tcg");

        var levels = LevelsParser.Parse(levelsText);
        var tcg = TcgParser.Parse(tcgText);
        var model = SemanticLinker.Link(levels, tcg);

        Assert.IsFalse(levels.HasErrors);
        Assert.IsFalse(tcg.HasErrors);
        Assert.AreEqual(levelsText, levels.Text);
        Assert.AreEqual(tcgText, tcg.Text);
        Assert.HasCount(1, model.LevelSets);
        Assert.AreEqual("SAMPLE_LEVEL", model.LevelSets[0].LevelSet.Name);
        Assert.HasCount(3, model.LevelSets[0].LevelSet.Entries);
        Assert.HasCount(1, model.LevelSets[0].Groups);
        Assert.AreEqual("SAMPLE_SPECS", model.LevelSets[0].Groups[0].SpecificationSet?.Name);
        Assert.AreEqual("low", model.LevelSets[0].Groups[0].TestConditions[0].Selector);
    }

    [TestMethod]
    public void ExpressionSpansIdentifyOnlyEditableValues()
    {
        const string text = "Version 1.0;\nLevels L { pins { VForce = 0.75V; OPMode = \"VSIM\"; } SequenceBreak 2mS; }";

        var document = LevelsParser.Parse(text);
        var resource = (ResourceEntrySyntax)document.LevelSets[0].Entries[0];
        var sequenceBreak = (SequenceBreakSyntax)document.LevelSets[0].Entries[1];

        Assert.AreEqual("0.75V", Slice(text, resource.Properties[0].ExpressionSpan));
        Assert.AreEqual("\"VSIM\"", Slice(text, resource.Properties[1].ExpressionSpan));
        Assert.AreEqual("2mS", Slice(text, sequenceBreak.ExpressionSpan));
    }

    [TestMethod]
    public void MissingSemanticReferencesProduceDiagnostics()
    {
        const string levelsText = "Version 1.0;\nLevels PRESENT { }";
        const string tcgText = "Version 1.0;\nTestConditionGroup G { SpecificationSet = MISSING_SPEC; Level = __main__::MISSING_LEVEL; }\nTestCondition T { TestConditionGroup = MISSING_GROUP; Selector = x; }";

        var model = SemanticLinker.Link(LevelsParser.Parse(levelsText), TcgParser.Parse(tcgText));

        Assert.IsTrue(model.Diagnostics.Any(item => item.Message.Contains("missing level", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(model.Diagnostics.Any(item => item.Message.Contains("missing group", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ProductionDocumentsParseAndLinkWithinInteractiveBudget()
    {
        var repositoryRoot = FindRepositoryRoot();
        var levelsText = File.ReadAllText(Path.Combine(repositoryRoot, "Shared", "Common", "LevelsSequences.lvl"));
        var tcgText = File.ReadAllText(Path.Combine(repositoryRoot, "Shared", "Common", "BaseLevels.tcg"));
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();

        var model = SemanticLinker.Link(LevelsParser.Parse(levelsText), TcgParser.Parse(tcgText));

        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.IsGreaterThan(100, model.LevelSets.Count);
        Assert.IsLessThan(2000L, stopwatch.ElapsedMilliseconds, "Initial parse must remain suitable for an interactive viewer.");
        Assert.IsLessThan(150L * 1024 * 1024, allocatedBytes, "Initial parsing must not allocate excessive memory.");
        Assert.AreEqual(levelsText, model.LevelsDocument.Text);
        Assert.AreEqual(tcgText, model.TcgDocument.Text);
        TestContext.WriteLine($"Production parse/link: {stopwatch.ElapsedMilliseconds} ms, {allocatedBytes / (1024d * 1024d):F2} MiB allocated");
    }

    public TestContext TestContext { get; set; } = null!;

    private static string ReadFixture(string name)
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), "CombinedLevelsViewer", "tests", "fixtures", name));
    }

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

    private static string Slice(string text, TextSpan span) => text.Substring(span.Start, span.Length);
}
