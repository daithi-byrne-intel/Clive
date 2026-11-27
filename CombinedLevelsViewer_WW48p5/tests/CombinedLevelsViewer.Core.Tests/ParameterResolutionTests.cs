using CombinedLevelsViewer.Core;

namespace CombinedLevelsViewer.Core.Tests;

[TestClass]
public sealed class ParameterResolutionTests
{
    private const string UserVars = "Version 1.0;\nUserVars TorchRulesVars { Current u_rail_clamp_hi = 4.00A; Current u_rail_clamp_lo = -5.00A; Double u_ratio = 1.05; }";

    private const string Specification =
        "Voltage p_rail_spec = 0.95V,0.52V,0.8V; "
        + "Double p_ratio = 1.0; "
        + "Voltage p_offset = 50mV; "
        + "Voltage c_rail_vih = p_rail_spec*p_ratio+p_offset; "
        + "Voltage c_rail_scaled = p_rail_spec*u_ratio; "
        + "Voltage c_rail_alias = p_rail_spec; "
        + "Current c_rail_clamp_hi_final = u_rail_clamp_hi; "
        + "Current c_rail_clamp_lo_final = u_rail_clamp_lo; "
        + "Voltage c_bad_count = 1V,2V; "
        + "Voltage c_missing = p_not_defined*p_ratio;";

    [TestMethod]
    public void ListedValueYieldsTheEntryOfTheSelector()
    {
        var group = Group();
        var resolver = new ParameterResolver(UserVarsParser.Parse(UserVars));

        Assert.AreEqual("0.95V", resolver.Resolve("p_rail_spec", group, "max_lvl").Text);
        Assert.AreEqual("0.52V", resolver.Resolve("p_rail_spec", group, "min_lvl").Text);
        Assert.AreEqual("0.8V", resolver.Resolve("p_rail_spec", group, "nom_lvl").Text);
    }

    [TestMethod]
    public void FormulaIsCalculatedInBaseUnits()
    {
        var group = Group();
        var resolver = new ParameterResolver(UserVarsParser.Parse(UserVars));

        var nominal = resolver.Resolve("c_rail_vih", group, "nom_lvl");
        var minimum = resolver.Resolve("c_rail_vih", group, "min_lvl");
        var scaled = resolver.Resolve("c_rail_scaled", group, "nom_lvl");

        Assert.IsNull(nominal.Problem);
        Assert.AreEqual("0.85V", nominal.Text);
        Assert.AreEqual("0.57V", minimum.Text);
        Assert.AreEqual("0.84V", scaled.Text);
        Assert.IsTrue(nominal.Trace.Contains("p_offset = 50mV", StringComparison.Ordinal), nominal.Trace);
    }

    [TestMethod]
    public void UserVarValueKeepsItsSpelling()
    {
        var group = Group();
        var resolver = new ParameterResolver(UserVarsParser.Parse(UserVars));

        Assert.AreEqual("4.00A", resolver.Resolve("c_rail_clamp_hi_final", group, "nom_lvl").Text);
        Assert.AreEqual("-5.00A", resolver.Resolve("c_rail_clamp_lo_final", group, "nom_lvl").Text);
    }

    [TestMethod]
    public void LiteralsAreLeftExactlyAsWritten()
    {
        var group = Group();
        var resolver = new ParameterResolver(UserVarsParser.Parse(UserVars));

        foreach (var literal in new[] { "500mA", "-0.04", "\"VSIM\"", "FALSE", "1mS" })
        {
            var resolved = resolver.Resolve(literal, group, "nom_lvl");
            Assert.AreEqual(literal, resolved.Text);
            Assert.IsFalse(resolved.Changed);
            Assert.IsNull(resolved.Problem);
        }
    }

    [TestMethod]
    public void NamesWithoutAnExactDefinitionAreReported()
    {
        var group = Group();
        var resolver = new ParameterResolver(UserVarsParser.Parse(UserVars));

        var unknown = resolver.Resolve("p_rail_spec_typo", group, "nom_lvl");
        var nested = resolver.Resolve("c_missing", group, "nom_lvl");
        var casing = resolver.Resolve("U_RAIL_CLAMP_HI", group, "nom_lvl");

        Assert.IsNotNull(unknown.Problem);
        Assert.AreEqual("p_rail_spec_typo", unknown.Text);
        Assert.IsNotNull(nested.Problem);
        Assert.IsTrue(nested.Problem.Contains("'p_not_defined'", StringComparison.Ordinal), nested.Problem);
        Assert.IsNotNull(casing.Problem);
        Assert.IsTrue(casing.Problem.Contains("differs only in letter case", StringComparison.Ordinal), casing.Problem);
    }

    [TestMethod]
    public void WrongNumberOfListedValuesOrUnknownSelectorIsReported()
    {
        var group = Group();
        var resolver = new ParameterResolver(UserVarsParser.Parse(UserVars));

        Assert.IsNotNull(resolver.Resolve("c_bad_count", group, "nom_lvl").Problem);
        Assert.IsNotNull(resolver.Resolve("p_rail_spec", group, "NOM_LVL").Problem);
    }

    [TestMethod]
    public void AuditCountsEveryTestCondition()
    {
        var model = Model("Version 1.0;\nLevels L { pins { VIH = c_rail_vih; VForce = c_missing; IClampHi = 500mA; } }");

        var audit = ParameterResolver.Audit(model, UserVarsParser.Parse(UserVars));

        // Two parameter cells for each of the three test conditions; the literal is not a reference.
        Assert.AreEqual(6, audit.References);
        Assert.AreEqual(3, audit.Unresolved);
        Assert.HasCount(1, audit.Problems);
    }

    [TestMethod]
    public void EditTargetIsTheSelectorEntryThatHoldsTheLiteral()
    {
        var group = Group();
        var resolver = new ParameterResolver(UserVarsParser.Parse(UserVars));

        var direct = resolver.FindEditTarget("p_rail_spec", group, "min_lvl", TcgText);
        var throughAlias = resolver.FindEditTarget("c_rail_alias", group, "nom_lvl", TcgText);
        var shared = resolver.FindEditTarget("p_offset", group, "nom_lvl", TcgText);

        Assert.IsTrue(direct.Editable, direct.Reason);
        Assert.AreEqual(PairedDocumentKind.TestConditions, direct.Kind);
        Assert.AreEqual("0.52V", TcgText.Substring(direct.Span.Start, direct.Span.Length));
        Assert.IsFalse(direct.AppliesToAllSelectors);
        Assert.IsTrue(throughAlias.Editable, throughAlias.Reason);
        Assert.AreEqual("0.8V", TcgText.Substring(throughAlias.Span.Start, throughAlias.Span.Length));
        Assert.IsTrue(throughAlias.Owner.Contains("p_rail_spec", StringComparison.Ordinal), throughAlias.Owner);
        Assert.IsTrue(shared.Editable, shared.Reason);
        Assert.AreEqual("50mV", TcgText.Substring(shared.Span.Start, shared.Span.Length));
        Assert.IsTrue(shared.AppliesToAllSelectors);
    }

    [TestMethod]
    public void EditingTheTargetChangesOnlyThatSelector()
    {
        var userVars = UserVarsParser.Parse(UserVars);
        var target = new ParameterResolver(userVars).FindEditTarget("c_rail_alias", Group(), "min_lvl", TcgText);
        var edited = TcgText.Substring(0, target.Span.Start) + "0.55V" + TcgText.Substring(target.Span.End);

        var group = SemanticLinker.Link(LevelsParser.Parse(LevelsText), TcgParser.Parse(edited)).LevelSets[0].Groups[0];
        var resolver = new ParameterResolver(userVars);

        Assert.AreEqual("0.55V", resolver.Resolve("c_rail_alias", group, "min_lvl").Text);
        Assert.AreEqual("0.95V", resolver.Resolve("c_rail_alias", group, "max_lvl").Text);
        Assert.AreEqual("0.8V", resolver.Resolve("c_rail_alias", group, "nom_lvl").Text);
        Assert.AreEqual(TcgText.Length, edited.Length);
    }

    [TestMethod]
    public void LiteralsAreEditedInTheLevelAndDerivedValuesAreNotEditable()
    {
        var group = Group();
        var resolver = new ParameterResolver(UserVarsParser.Parse(UserVars));

        var literal = resolver.FindEditTarget("500mA", group, "nom_lvl", TcgText);
        var formula = resolver.FindEditTarget("c_rail_vih", group, "nom_lvl", TcgText);
        var userVar = resolver.FindEditTarget("c_rail_clamp_hi_final", group, "nom_lvl", TcgText);
        var staleText = resolver.FindEditTarget("p_rail_spec", group, "nom_lvl", " " + TcgText);

        Assert.IsTrue(literal.Editable);
        Assert.AreEqual(PairedDocumentKind.Levels, literal.Kind);
        Assert.IsFalse(formula.Editable);
        Assert.IsTrue(formula.Reason!.Contains("calculated", StringComparison.Ordinal), formula.Reason);
        Assert.IsFalse(userVar.Editable);
        Assert.IsTrue(userVar.Reason!.Contains("uservar 'u_rail_clamp_hi'", StringComparison.Ordinal), userVar.Reason);
        Assert.IsFalse(staleText.Editable);
    }

    private const string LevelsText = "Version 1.0;\nLevels L { pins { VIH = c_rail_vih; } }";

    private static readonly string TcgText = "Version 1.0;\nSpecificationSet S (max_lvl,min_lvl,nom_lvl) { " + Specification + " }\n"
        + "TestConditionGroup G { SpecificationSet = S; Level = __main__::L; }\n"
        + "TestCondition L_max_lvl { TestConditionGroup = G; Selector = max_lvl; }\n"
        + "TestCondition L_min_lvl { TestConditionGroup = G; Selector = min_lvl; }\n"
        + "TestCondition L_nom_lvl { TestConditionGroup = G; Selector = nom_lvl; }";

    private static LinkedTestConditionGroup Group() => Model(LevelsText).LevelSets[0].Groups[0];

    private static CombinedLevelsModel Model(string levelsText)
    {
        var levels = LevelsParser.Parse(levelsText);
        var conditions = TcgParser.Parse(TcgText);
        Assert.IsFalse(levels.HasErrors, "levels fixture must parse");
        Assert.IsFalse(conditions.HasErrors, "tcg fixture must parse");
        return SemanticLinker.Link(levels, conditions);
    }
}
