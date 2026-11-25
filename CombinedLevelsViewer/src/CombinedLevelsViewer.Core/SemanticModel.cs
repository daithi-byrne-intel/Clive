using System;
using System.Collections.Generic;

namespace CombinedLevelsViewer.Core;

public sealed class CombinedLevelsModel
{
    public CombinedLevelsModel(
        LevelsDocument levelsDocument,
        TcgDocument tcgDocument,
        IReadOnlyList<LinkedLevelSet> levelSets,
        IReadOnlyList<DocumentDiagnostic> diagnostics)
    {
        LevelsDocument = levelsDocument;
        TcgDocument = tcgDocument;
        LevelSets = levelSets;
        Diagnostics = diagnostics;
    }

    public LevelsDocument LevelsDocument { get; }
    public TcgDocument TcgDocument { get; }
    public IReadOnlyList<LinkedLevelSet> LevelSets { get; }
    public IReadOnlyList<DocumentDiagnostic> Diagnostics { get; }
}

public sealed class LinkedLevelSet
{
    public LinkedLevelSet(LevelSetSyntax levelSet, IReadOnlyList<LinkedTestConditionGroup> groups)
    {
        LevelSet = levelSet;
        Groups = groups;
    }

    public LevelSetSyntax LevelSet { get; }
    public IReadOnlyList<LinkedTestConditionGroup> Groups { get; }
}

public sealed class LinkedTestConditionGroup
{
    public LinkedTestConditionGroup(
        TestConditionGroupSyntax group,
        SpecificationSetSyntax? specificationSet,
        IReadOnlyList<TestConditionSyntax> testConditions)
    {
        Group = group;
        SpecificationSet = specificationSet;
        TestConditions = testConditions;
    }

    public TestConditionGroupSyntax Group { get; }
    public SpecificationSetSyntax? SpecificationSet { get; }
    public IReadOnlyList<TestConditionSyntax> TestConditions { get; }
}

public static class SemanticLinker
{
    public static CombinedLevelsModel Link(LevelsDocument levels, TcgDocument tcg)
    {
        if (levels == null) throw new ArgumentNullException(nameof(levels));
        if (tcg == null) throw new ArgumentNullException(nameof(tcg));

        var diagnostics = new List<DocumentDiagnostic>();
        AddRange(diagnostics, levels.Diagnostics);
        AddRange(diagnostics, tcg.Diagnostics);

        var levelsByName = IndexUnique(levels.LevelSets, item => item.Name, item => item.Span, "level", diagnostics);
        var specificationsByName = IndexUnique(tcg.SpecificationSets, item => item.Name, item => item.Span, "specification set", diagnostics);
        var groupsByName = IndexUnique(tcg.Groups, item => item.Name, item => item.Span, "test-condition group", diagnostics);
        var linkedGroups = new Dictionary<string, List<LinkedTestConditionGroup>>(StringComparer.Ordinal);
        var conditionsByGroup = new Dictionary<string, List<TestConditionSyntax>>(StringComparer.Ordinal);

        foreach (var condition in tcg.TestConditions)
        {
            if (!conditionsByGroup.TryGetValue(condition.GroupName, out var conditions))
            {
                conditions = new List<TestConditionSyntax>();
                conditionsByGroup.Add(condition.GroupName, conditions);
            }
            conditions.Add(condition);

            if (!groupsByName.ContainsKey(condition.GroupName))
            {
                diagnostics.Add(new DocumentDiagnostic("Test condition '" + condition.Name + "' references missing group '" + condition.GroupName + "'.", condition.Span));
            }
        }

        foreach (var group in tcg.Groups)
        {
            levelsByName.TryGetValue(group.LevelName, out var level);
            specificationsByName.TryGetValue(group.SpecificationSetName, out var specification);
            conditionsByGroup.TryGetValue(group.Name, out var conditions);
            conditions = conditions ?? new List<TestConditionSyntax>();

            if (level == null)
            {
                diagnostics.Add(new DocumentDiagnostic("Test-condition group '" + group.Name + "' references missing level '" + group.LevelReference + "'.", group.Span));
                continue;
            }

            if (specification == null)
            {
                diagnostics.Add(new DocumentDiagnostic("Test-condition group '" + group.Name + "' references missing specification set '" + group.SpecificationSetName + "'.", group.Span));
            }
            else
            {
                ValidateSelectors(group, specification, conditions, diagnostics);
            }

            if (!linkedGroups.TryGetValue(level.Name, out var list))
            {
                list = new List<LinkedTestConditionGroup>();
                linkedGroups.Add(level.Name, list);
            }
            list.Add(new LinkedTestConditionGroup(group, specification, conditions));
        }

        var linkedLevels = new List<LinkedLevelSet>();
        foreach (var level in levels.LevelSets)
        {
            linkedGroups.TryGetValue(level.Name, out var groups);
            linkedLevels.Add(new LinkedLevelSet(level, groups ?? new List<LinkedTestConditionGroup>()));
        }

        return new CombinedLevelsModel(levels, tcg, linkedLevels, diagnostics);
    }

    private static void ValidateSelectors(
        TestConditionGroupSyntax group,
        SpecificationSetSyntax specification,
        IReadOnlyList<TestConditionSyntax> conditions,
        List<DocumentDiagnostic> diagnostics)
    {
        var selectorSet = new HashSet<string>(specification.Selectors, StringComparer.Ordinal);
        foreach (var condition in conditions)
        {
            if (!selectorSet.Contains(condition.Selector))
            {
                diagnostics.Add(new DocumentDiagnostic(
                    "Test condition '" + condition.Name + "' selects '" + condition.Selector + "', which is absent from specification set '" + specification.Name + "'.",
                    condition.Span));
            }
        }

        foreach (var value in specification.Values)
        {
            var count = CountTopLevelValues(value.Expression);
            if (count != 1 && count != specification.Selectors.Count)
            {
                diagnostics.Add(new DocumentDiagnostic(
                    "Specification value '" + value.Name + "' has " + count + " values for " + specification.Selectors.Count + " selectors in group '" + group.Name + "'.",
                    value.Span,
                    DiagnosticSeverity.Warning));
            }
        }
    }

    public static IReadOnlyList<string> SplitTopLevelValues(string expression)
    {
        var values = new List<string>();
        var start = 0;
        var depth = 0;
        var quoted = false;
        for (var i = 0; i < expression.Length; i++)
        {
            var current = expression[i];
            if (current == '"' && (i == 0 || expression[i - 1] != '\\')) quoted = !quoted;
            if (quoted) continue;
            if (current == '(') depth++;
            if (current == ')') depth--;
            if (current == ',' && depth == 0)
            {
                values.Add(expression.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }
        values.Add(expression.Substring(start).Trim());
        return values;
    }

    private static int CountTopLevelValues(string expression) => SplitTopLevelValues(expression).Count;

    private static Dictionary<string, T> IndexUnique<T>(
        IReadOnlyList<T> items,
        Func<T, string> name,
        Func<T, TextSpan> span,
        string kind,
        List<DocumentDiagnostic> diagnostics)
        where T : class
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var itemName = name(item);
            if (result.ContainsKey(itemName))
            {
                diagnostics.Add(new DocumentDiagnostic("Duplicate " + kind + " '" + itemName + "'.", span(item)));
            }
            else
            {
                result.Add(itemName, item);
            }
        }
        return result;
    }

    private static void AddRange<T>(List<T> destination, IReadOnlyList<T> source)
    {
        foreach (var item in source) destination.Add(item);
    }
}
