using CombinedLevelsViewer.Core;

namespace CombinedLevelsViewer.Core.Tests;

[TestClass]
public sealed class ClampResolutionTests
{
    private const string UserVars = """
        Version 1.0;

        UserVars Other
        {
            String Decision = "A;B"; # semicolon inside a string
        }

        SelectorRuleCollection Rules
        {
            SelectorRule R(x)
            {
                x => Other.Decision == "A;B";
            }
        }

        UserVars TorchRulesVars
        {
            Current u_rail_clamp_hi = 4.00A;
            Current u_rail_clamp_hi_sdt = 1.80A;
            #Current u_rail_clamp_lo = -9A;
            Current u_rail_clamp_lo = -5.00A; # trailing comment
        }
        """;

    private const string Levels = "Version 1.0;\nLevels L { pins { IClampHi = c_rail_clamp_hi_final; IClampLo = c_rail_clamp_lo_final; IRange = \"IR40mA\"; } other { IClampHi = 500mA; } }";

    [TestMethod]
    public void ParserReadsOnlyUserVarsCollections()
    {
        var document = UserVarsParser.Parse(UserVars);

        Assert.HasCount(4, document.Definitions);
        Assert.AreEqual("\"A;B\"", document.FindExact("Decision")[0].Expression);
        Assert.AreEqual("TorchRulesVars", document.FindExact("u_rail_clamp_hi")[0].Collection);
        Assert.AreEqual("-5.00A", document.FindExact("u_rail_clamp_lo")[0].Expression);
        Assert.HasCount(0, document.FindExact("x"));
    }

    [TestMethod]
    public void ClampResolvesThroughSpecificationSetToExactUserVar()
    {
        var model = Link("Current c_rail_clamp_hi_final = u_rail_clamp_hi; Current c_rail_clamp_lo_final = u_rail_clamp_lo;");

        var resolution = ClampResolver.Resolve("c_rail_clamp_hi_final", model.LevelSets[0].Groups, UserVarsParser.Parse(UserVars));

        Assert.IsNotNull(resolution);
        Assert.IsFalse(resolution.HasProblem);
        Assert.AreEqual("u_rail_clamp_hi = 4.00A", resolution.Summary);
    }

    [TestMethod]
    public void LiteralClampHasNothingToResolve()
    {
        var model = Link("Current c_rail_clamp_hi_final = u_rail_clamp_hi;");

        Assert.IsNull(ClampResolver.Resolve("500mA", model.LevelSets[0].Groups, UserVarsParser.Parse(UserVars)));
        Assert.IsNull(ClampResolver.Resolve("-0.04", model.LevelSets[0].Groups, UserVarsParser.Parse(UserVars)));
    }

    [TestMethod]
    public void NearMissNamesAreNotMatched()
    {
        var userVars = UserVarsParser.Parse(UserVars);

        // A prefix of an existing uservar, and a different letter case, must both be rejected.
        var prefix = ClampResolver.Resolve("c_rail_clamp_hi_final", Link("Current c_rail_clamp_hi_final = u_rail_clamp;").LevelSets[0].Groups, userVars);
        var casing = ClampResolver.Resolve("c_rail_clamp_hi_final", Link("Current c_rail_clamp_hi_final = U_Rail_Clamp_Hi;").LevelSets[0].Groups, userVars);

        Assert.IsNotNull(prefix);
        Assert.IsTrue(prefix.HasProblem);
        Assert.IsTrue(prefix.Summary.Contains("no uservar is named exactly 'u_rail_clamp'", StringComparison.Ordinal), prefix.Summary);
        Assert.IsNotNull(casing);
        Assert.IsTrue(casing.HasProblem);
        Assert.IsTrue(casing.Summary.Contains("differs only in letter case", StringComparison.Ordinal), casing.Summary);
    }

    [TestMethod]
    public void ClampMissingFromSpecificationSetIsReported()
    {
        var model = Link("Current c_rail_clamp_hi_final = u_rail_clamp_hi;");

        var resolution = ClampResolver.Resolve("c_rail_clamp_lo_final", model.LevelSets[0].Groups, UserVarsParser.Parse(UserVars));

        Assert.IsNotNull(resolution);
        Assert.IsTrue(resolution.HasProblem);
        Assert.IsTrue(resolution.Summary.Contains("is not defined in specification set 'S'", StringComparison.Ordinal), resolution.Summary);
    }

    [TestMethod]
    public void AuditCountsReferencesAndUnresolvedClamps()
    {
        var userVars = UserVarsParser.Parse(UserVars);

        var complete = ClampResolver.Audit(Link("Current c_rail_clamp_hi_final = u_rail_clamp_hi; Current c_rail_clamp_lo_final = u_rail_clamp_lo;"), userVars);
        var incomplete = ClampResolver.Audit(Link("Current c_rail_clamp_hi_final = u_rail_clamp_hi; Current c_rail_clamp_lo_final = u_missing;"), userVars);

        Assert.AreEqual(2, complete.References);
        Assert.AreEqual(0, complete.Unresolved);
        Assert.AreEqual(2, incomplete.References);
        Assert.AreEqual(1, incomplete.Unresolved);
        Assert.IsNotNull(incomplete.FirstProblem);
        Assert.IsTrue(incomplete.FirstProblem.Contains("IClampLo", StringComparison.Ordinal), incomplete.FirstProblem);
    }

    private static CombinedLevelsModel Link(string specificationValues)
    {
        var tcg = "Version 1.0;\nSpecificationSet S (sel) { " + specificationValues + " }\nTestConditionGroup G { SpecificationSet = S; Level = __main__::L; }\nTestCondition T { TestConditionGroup = G; Selector = sel; }";
        var levels = LevelsParser.Parse(Levels);
        var conditions = TcgParser.Parse(tcg);
        Assert.IsFalse(levels.HasErrors, "levels fixture must parse");
        Assert.IsFalse(conditions.HasErrors, "tcg fixture must parse");
        return SemanticLinker.Link(levels, conditions);
    }
}
