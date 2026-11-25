using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CombinedLevelsViewer.Core;

/// <summary>How one linked test-condition group resolves a clamp referenced by a level.</summary>
public sealed class ClampUserVarMatch
{
    public ClampUserVarMatch(string groupName, string specificationSetName, string resolved, string? problem)
    {
        GroupName = groupName;
        SpecificationSetName = specificationSetName;
        Resolved = resolved;
        Problem = problem;
    }

    public string GroupName { get; }
    public string SpecificationSetName { get; }

    /// <summary>"uservar = value" when resolved, otherwise empty.</summary>
    public string Resolved { get; }

    /// <summary>Why the clamp could not be matched exactly, or null when it was.</summary>
    public string? Problem { get; }
}

public sealed class ClampResolution
{
    public ClampResolution(string levelExpression, IReadOnlyList<ClampUserVarMatch> matches)
    {
        LevelExpression = levelExpression;
        Matches = matches;

        var distinct = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var details = new List<string>();
        foreach (var match in matches)
        {
            if (match.Problem != null) HasProblem = true;
            var text = match.Problem != null ? ClampResolver.NoExactMatchPrefix + match.Problem : match.Resolved;
            if (seen.Add(text)) distinct.Add(text);
            details.Add(match.GroupName.Length == 0 ? text : match.GroupName + " (" + match.SpecificationSetName + "): " + text);
        }

        Summary = string.Join(" | ", distinct);
        Details = string.Join("\n", details);
    }

    public string LevelExpression { get; }
    public IReadOnlyList<ClampUserVarMatch> Matches { get; }
    public bool HasProblem { get; }

    /// <summary>The distinct results across all linked groups, for display in one cell.</summary>
    public string Summary { get; }

    /// <summary>One line per linked group.</summary>
    public string Details { get; }
}

public sealed class ClampAudit
{
    public ClampAudit(int references, int unresolved, string? firstProblem)
    {
        References = references;
        Unresolved = unresolved;
        FirstProblem = firstProblem;
    }

    /// <summary>Clamp cells in the levels file that name a parameter instead of holding a literal value.</summary>
    public int References { get; }

    /// <summary>How many of those have no exact uservar match in at least one linked group.</summary>
    public int Unresolved { get; }

    public string? FirstProblem { get; }
}

/// <summary>
/// Pulls clamp values in from the uservars. A clamp in a level names a specification-set variable
/// (c_..._final); the linked specification set assigns it a uservar (u_...); the uservar file holds the value.
/// Every name in that chain must match exactly: comparison is case-sensitive and never partial, because
/// the uservar file holds many near-identical names that differ only by a suffix.
/// </summary>
public static class ClampResolver
{
    public const string NoExactMatchPrefix = "NO EXACT MATCH: ";

    private static readonly Regex Identifier = new Regex(@"^[A-Za-z_]\w*(\.[A-Za-z_]\w*)?$", RegexOptions.CultureInvariant);

    public static bool IsClampProperty(string propertyName)
    {
        return propertyName != null && propertyName.IndexOf("Clamp", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Resolves the clamp expression of one level cell. Returns null when the level holds a literal
    /// value (for example 500mA), because then there is nothing to pull in.
    /// </summary>
    public static ClampResolution? Resolve(string levelExpression, IReadOnlyList<LinkedTestConditionGroup> groups, UserVarsDocument userVars)
    {
        if (groups == null) throw new ArgumentNullException(nameof(groups));
        if (userVars == null) throw new ArgumentNullException(nameof(userVars));
        var parameter = (levelExpression ?? string.Empty).Trim();
        if (!Identifier.IsMatch(parameter)) return null;

        var matches = new List<ClampUserVarMatch>();
        if (groups.Count == 0)
        {
            // No specification set to go through: the level may name the uservar itself.
            matches.Add(ResolveUserVar(parameter, string.Empty, string.Empty, userVars, "'" + parameter + "' has no linked test-condition group, and "));
            return new ClampResolution(parameter, matches);
        }

        foreach (var group in groups)
        {
            var groupName = group.Group.Name;
            var specification = group.SpecificationSet;
            if (specification == null)
            {
                matches.Add(new ClampUserVarMatch(groupName, group.Group.SpecificationSetName, string.Empty, "specification set '" + group.Group.SpecificationSetName + "' is missing"));
                continue;
            }

            TypedValueSyntax? specValue = null;
            foreach (var value in specification.Values)
            {
                if (string.Equals(value.Name, parameter, StringComparison.Ordinal))
                {
                    specValue = value;
                    break;
                }
            }

            if (specValue == null)
            {
                matches.Add(ResolveUserVar(parameter, groupName, specification.Name, userVars, "'" + parameter + "' is not defined in specification set '" + specification.Name + "', and "));
                continue;
            }

            var parts = SemanticLinker.SplitTopLevelValues(specValue.Expression);
            foreach (var part in parts)
            {
                matches.Add(Identifier.IsMatch(part)
                    ? ResolveUserVar(part, groupName, specification.Name, userVars, string.Empty)
                    : new ClampUserVarMatch(groupName, specification.Name, part + " (set in specification set, not a uservar)", null));
            }
        }

        return new ClampResolution(parameter, matches);
    }

    /// <summary>Checks every clamp reference in the levels file against the uservars.</summary>
    public static ClampAudit Audit(CombinedLevelsModel model, UserVarsDocument userVars)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        var references = 0;
        var unresolved = 0;
        string? firstProblem = null;
        foreach (var level in model.LevelSets)
        {
            foreach (var entry in level.LevelSet.Entries)
            {
                if (!(entry is ResourceEntrySyntax resource)) continue;
                foreach (var property in resource.Properties)
                {
                    if (!IsClampProperty(property.Name)) continue;
                    var resolution = Resolve(property.Expression, level.Groups, userVars);
                    if (resolution == null) continue;
                    references++;
                    if (!resolution.HasProblem) continue;
                    unresolved++;
                    firstProblem ??= level.LevelSet.Name + " / " + resource.Name + " / " + property.Name + ": " + resolution.Summary;
                }
            }
        }

        return new ClampAudit(references, unresolved, firstProblem);
    }

    private static ClampUserVarMatch ResolveUserVar(string reference, string groupName, string specificationSetName, UserVarsDocument userVars, string problemPrefix)
    {
        var separator = reference.LastIndexOf('.');
        var collection = separator < 0 ? null : reference.Substring(0, separator);
        var name = separator < 0 ? reference : reference.Substring(separator + 1);

        var candidates = new List<UserVarDefinition>();
        foreach (var definition in userVars.FindExact(name))
        {
            if (collection == null || string.Equals(definition.Collection, collection, StringComparison.Ordinal)) candidates.Add(definition);
        }

        if (candidates.Count == 1)
        {
            return new ClampUserVarMatch(groupName, specificationSetName, candidates[0].Name + " = " + candidates[0].Expression, null);
        }

        if (candidates.Count > 1)
        {
            var collections = new List<string>();
            foreach (var candidate in candidates) collections.Add(candidate.Collection);
            return new ClampUserVarMatch(groupName, specificationSetName, string.Empty, problemPrefix + "uservar '" + reference + "' is defined more than once (" + string.Join(", ", collections) + ")");
        }

        var problem = problemPrefix + "no uservar is named exactly '" + reference + "'";
        foreach (var near in userVars.FindIgnoringCase(name))
        {
            problem += " ('" + near.Name + "' differs only in letter case)";
            break;
        }
        return new ClampUserVarMatch(groupName, specificationSetName, string.Empty, problem);
    }
}
