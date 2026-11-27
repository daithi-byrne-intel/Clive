using CombinedLevelsViewer.Core;

namespace CombinedLevelsViewer.Core.Tests;

[TestClass]
public sealed class DuplicateAnalysisTests
{
    // A and B give the same values through different parameter names and unit spellings.
    // C has only the nom selector of A. D differs from A in one literal.
    private const string Levels = "Version 1.0;\n"
        + "Levels A { pins { VIH = p_v; IClampHi = 500mA; } }\n"
        + "Levels B { pins { VIH = p_w; IClampHi = 0.5A; } }\n"
        + "Levels C { pins { VIH = p_v; IClampHi = 500mA; } }\n"
        + "Levels D { pins { VIH = p_v; IClampHi = 1A; } }";

    private const string Conditions = "Version 1.0;\n"
        + "SpecificationSet SA (max,nom) { Voltage p_v = 0.9V,0.8V; }\n"
        + "SpecificationSet SB (max,nom) { Voltage p_w = 900mV,800mV; }\n"
        + "SpecificationSet SC (nom) { Voltage p_v = 0.8V; }\n"
        + "SpecificationSet SD (max,nom) { Voltage p_v = 0.9V,0.8V; }\n"
        + "TestConditionGroup GA { SpecificationSet = SA; Level = __main__::A; }\n"
        + "TestConditionGroup GB { SpecificationSet = SB; Level = __main__::B; }\n"
        + "TestConditionGroup GC { SpecificationSet = SC; Level = __main__::C; }\n"
        + "TestConditionGroup GD { SpecificationSet = SD; Level = __main__::D; }\n"
        + "TestCondition A_max { TestConditionGroup = GA; Selector = max; }\n"
        + "TestCondition A_nom { TestConditionGroup = GA; Selector = nom; }\n"
        + "TestCondition B_max { TestConditionGroup = GB; Selector = max; }\n"
        + "TestCondition B_nom { TestConditionGroup = GB; Selector = nom; }\n"
        + "TestCondition C_nom { TestConditionGroup = GC; Selector = nom; }\n"
        + "TestCondition D_max { TestConditionGroup = GD; Selector = max; }\n"
        + "TestCondition D_nom { TestConditionGroup = GD; Selector = nom; }";

    [TestMethod]
    public void LevelSetsWithEqualResolvedValuesAreDuplicates()
    {
        var analysis = Analyze();

        var forA = analysis.ForLevel("A");
        Assert.HasCount(2, forA);
        Assert.AreEqual(LevelDuplicateRelation.Identical, forA.Single(item => item.OtherLevel == "B").Relation);
        Assert.AreEqual(LevelDuplicateRelation.Contains, forA.Single(item => item.OtherLevel == "C").Relation);

        var forC = analysis.ForLevel("C");
        Assert.HasCount(2, forC);
        Assert.IsTrue(forC.All(item => item.Relation == LevelDuplicateRelation.ContainedIn));
        Assert.AreEqual("nom", forC[0].SharedSelectors.Single());
    }

    [TestMethod]
    public void OneDifferentValueMeansNoDuplicate()
    {
        var analysis = Analyze();

        Assert.HasCount(0, analysis.ForLevel("D"));
        Assert.HasCount(0, analysis.ForCondition("D", "GD", "D_nom"));
    }

    [TestMethod]
    public void TestConditionsWithEqualTablesAreListedAcrossLevelSets()
    {
        var analysis = Analyze();

        var nominal = analysis.ForCondition("A", "GA", "A_nom").Select(item => item.Condition).OrderBy(name => name).ToList();
        var maximum = analysis.ForCondition("A", "GA", "A_max").Select(item => item.Condition).ToList();

        CollectionAssert.AreEqual(new[] { "B_nom", "C_nom" }, nominal);
        CollectionAssert.AreEqual(new[] { "B_max" }, maximum);
    }

    [TestMethod]
    public void NormalizeValueUsesBaseUnits()
    {
        Assert.AreEqual("0.8V", ParameterResolver.NormalizeValue("800mV"));
        Assert.AreEqual("0.8V", ParameterResolver.NormalizeValue("0.80V"));
        Assert.AreEqual("-0.5A", ParameterResolver.NormalizeValue("-500mA"));
        Assert.AreEqual("\"VSIM\"", ParameterResolver.NormalizeValue("\"VSIM\""));
        Assert.AreEqual("FALSE", ParameterResolver.NormalizeValue("FALSE"));
    }

    private static DuplicateAnalysis Analyze()
    {
        var levels = LevelsParser.Parse(Levels);
        var conditions = TcgParser.Parse(Conditions);
        Assert.IsFalse(levels.HasErrors, "levels fixture must parse");
        Assert.IsFalse(conditions.HasErrors, "tcg fixture must parse");
        return DuplicateAnalysis.Analyze(SemanticLinker.Link(levels, conditions), UserVarsDocument.Empty);
    }
}
