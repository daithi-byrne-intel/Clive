using System;
using System.Collections.Generic;
using System.Text;

namespace CombinedLevelsViewer.Core;

public enum LevelDuplicateRelation
{
    /// <summary>Same selectors, and the same values for every selector.</summary>
    Identical,

    /// <summary>Every selector of this level set exists in the other with the same values; the other has more.</summary>
    ContainedIn,

    /// <summary>Every selector of the other level set exists in this one with the same values; this one has more.</summary>
    Contains,
}

/// <summary>Another level set that produces the same values as this one.</summary>
public sealed class LevelDuplicate
{
    public LevelDuplicate(string otherLevel, LevelDuplicateRelation relation, IReadOnlyList<string> sharedSelectors)
    {
        OtherLevel = otherLevel;
        Relation = relation;
        SharedSelectors = sharedSelectors;
    }

    public string OtherLevel { get; }
    public LevelDuplicateRelation Relation { get; }

    /// <summary>The selectors both level sets have, all of which give identical values.</summary>
    public IReadOnlyList<string> SharedSelectors { get; }
}

/// <summary>One test condition of one level set.</summary>
public sealed class ConditionReference
{
    public ConditionReference(string level, string group, string condition, string selector)
    {
        Level = level;
        Group = group;
        Condition = condition;
        Selector = selector;
    }

    public string Level { get; }
    public string Group { get; }
    public string Condition { get; }
    public string Selector { get; }
}

/// <summary>
/// Finds level sets and test conditions that produce the same values under different names.
/// Every level set is resolved for every test condition; two are duplicates when every cell resolves to
/// the same value (prefixed units are normalised, so 800mV equals 0.8V). The result reflects the values
/// currently in the files: duplicates that get there through different parameters diverge when one changes.
/// </summary>
public sealed class DuplicateAnalysis
{
    private static readonly IReadOnlyList<LevelDuplicate> NoLevelDuplicates = new List<LevelDuplicate>();
    private static readonly IReadOnlyList<ConditionReference> NoConditionDuplicates = new List<ConditionReference>();

    private readonly Dictionary<string, List<LevelDuplicate>> levelDuplicates = new Dictionary<string, List<LevelDuplicate>>(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ConditionReference>> conditionDuplicates = new Dictionary<string, List<ConditionReference>>(StringComparer.Ordinal);

    private DuplicateAnalysis()
    {
    }

    public static DuplicateAnalysis Empty { get; } = new DuplicateAnalysis();

    /// <summary>Level sets that are identical to, contained in, or contain <paramref name="level"/>.</summary>
    public IReadOnlyList<LevelDuplicate> ForLevel(string level)
    {
        return levelDuplicates.TryGetValue(level, out var found) ? found : NoLevelDuplicates;
    }

    /// <summary>Other test conditions (in any level set) whose resolved table is identical.</summary>
    public IReadOnlyList<ConditionReference> ForCondition(string level, string group, string condition)
    {
        return conditionDuplicates.TryGetValue(ConditionKey(level, group, condition), out var found) ? found : NoConditionDuplicates;
    }

    public static DuplicateAnalysis Analyze(CombinedLevelsModel model, UserVarsDocument userVars)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        var resolver = new ParameterResolver(userVars);
        var analysis = new DuplicateAnalysis();

        // Resolved table of every test condition, as one comparable string.
        var levelNames = new List<string>();
        var selectorTables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var comparable = new HashSet<string>(StringComparer.Ordinal);
        var byTable = new Dictionary<string, List<ConditionReference>>(StringComparer.Ordinal);
        foreach (var level in model.LevelSets)
        {
            var levelName = level.LevelSet.Name;
            if (selectorTables.ContainsKey(levelName)) continue; // Duplicate level names are reported as diagnostics elsewhere.
            var table = LevelTableProjector.Project(level);
            var tables = new Dictionary<string, string>(StringComparer.Ordinal);
            var unambiguous = true;
            foreach (var group in level.Groups)
            {
                foreach (var condition in group.TestConditions)
                {
                    var resolved = Describe(table, group, condition.Selector, resolver);
                    var reference = new ConditionReference(levelName, group.Group.Name, condition.Name, condition.Selector);
                    if (!byTable.TryGetValue(resolved, out var sameTable))
                    {
                        sameTable = new List<ConditionReference>();
                        byTable.Add(resolved, sameTable);
                    }
                    sameTable.Add(reference);

                    // Level sets are compared selector by selector, which needs one table per selector name.
                    if (tables.TryGetValue(condition.Selector, out var existing))
                    {
                        if (!string.Equals(existing, resolved, StringComparison.Ordinal)) unambiguous = false;
                    }
                    else
                    {
                        tables.Add(condition.Selector, resolved);
                    }
                }
            }

            levelNames.Add(levelName);
            selectorTables.Add(levelName, tables);
            if (unambiguous && tables.Count > 0) comparable.Add(levelName);
        }

        foreach (var sameTable in byTable.Values)
        {
            if (sameTable.Count < 2) continue;
            foreach (var reference in sameTable)
            {
                var others = new List<ConditionReference>();
                foreach (var other in sameTable)
                {
                    if (!ReferenceEquals(other, reference)) others.Add(other);
                }
                analysis.conditionDuplicates[ConditionKey(reference.Level, reference.Group, reference.Condition)] = others;
            }
        }

        foreach (var first in levelNames)
        {
            if (!comparable.Contains(first)) continue;
            var firstTables = selectorTables[first];
            foreach (var second in levelNames)
            {
                if (string.Equals(first, second, StringComparison.Ordinal) || !comparable.Contains(second)) continue;
                var secondTables = selectorTables[second];
                var smaller = firstTables.Count <= secondTables.Count ? firstTables : secondTables;
                var larger = ReferenceEquals(smaller, firstTables) ? secondTables : firstTables;
                var shared = new List<string>();
                var matches = true;
                foreach (var pair in smaller)
                {
                    if (!larger.TryGetValue(pair.Key, out var other) || !string.Equals(other, pair.Value, StringComparison.Ordinal))
                    {
                        matches = false;
                        break;
                    }
                    shared.Add(pair.Key);
                }
                if (!matches) continue;

                var relation = firstTables.Count == secondTables.Count
                    ? LevelDuplicateRelation.Identical
                    : firstTables.Count < secondTables.Count ? LevelDuplicateRelation.ContainedIn : LevelDuplicateRelation.Contains;
                if (!analysis.levelDuplicates.TryGetValue(first, out var list))
                {
                    list = new List<LevelDuplicate>();
                    analysis.levelDuplicates.Add(first, list);
                }
                list.Add(new LevelDuplicate(second, relation, shared));
            }
        }

        return analysis;
    }

    private static string Describe(LevelTable table, LinkedTestConditionGroup group, string selector, ParameterResolver resolver)
    {
        var cache = new Dictionary<string, string>(StringComparer.Ordinal);
        var builder = new StringBuilder();
        foreach (var row in table.Rows)
        {
            builder.Append(row.Name).Append('{');
            foreach (var cell in row.Cells)
            {
                if (cell.Value.Length == 0) continue;
                var value = cell.Value;
                if (ParameterResolver.IsParameterProperty(cell.Column))
                {
                    if (!cache.TryGetValue(cell.Value, out value))
                    {
                        value = ParameterResolver.NormalizeValue(resolver.Resolve(cell.Value, group, selector).Text);
                        cache.Add(cell.Value, value);
                    }
                }
                builder.Append(cell.Column).Append('=').Append(value).Append(';');
            }
            builder.Append('}');
        }
        return builder.ToString();
    }

    private static string ConditionKey(string level, string group, string condition) => level + "\u001f" + group + "\u001f" + condition;
}
