using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CombinedLevelsViewer.Core;

/// <summary>The value of one level cell for one selector.</summary>
public sealed class ResolvedParameter
{
    public ResolvedParameter(string expression, string text, bool changed, string? problem, IReadOnlyList<string> steps)
    {
        Expression = expression;
        Text = text;
        Changed = changed;
        Problem = problem;
        Steps = steps;
    }

    /// <summary>The expression as written in the levels file.</summary>
    public string Expression { get; }

    /// <summary>The resolved value, or the original expression when it could not be resolved.</summary>
    public string Text { get; }

    /// <summary>True when the expression referenced at least one parameter that was substituted.</summary>
    public bool Changed { get; }

    /// <summary>Set when a referenced name has no exact definition.</summary>
    public string? Problem { get; }

    /// <summary>The substitutions made, in order, as "name = definition".</summary>
    public IReadOnlyList<string> Steps { get; }

    /// <summary>The expression, each substitution and the result, one per line.</summary>
    public string Trace
    {
        get
        {
            if (!Changed && Problem == null) return Text;
            var lines = new List<string> { Expression };
            lines.AddRange(Steps);
            lines.Add(Problem != null ? ParameterResolver.UnresolvedPrefix + Problem : "= " + Text);
            return string.Join("\n", lines);
        }
    }
}

/// <summary>Where the value shown for one level cell and selector is defined, so an edit can be written there.</summary>
public sealed class ParameterEditTarget
{
    private ParameterEditTarget(PairedDocumentKind kind, TextSpan span, string owner, bool appliesToAllSelectors, string? reason)
    {
        Kind = kind;
        Span = span;
        Owner = owner;
        AppliesToAllSelectors = appliesToAllSelectors;
        Reason = reason;
    }

    public bool Editable => Reason == null;

    /// <summary>The file that holds the value.</summary>
    public PairedDocumentKind Kind { get; }

    /// <summary>For a test-condition target: the text to replace. For a levels target the cell's own span is used.</summary>
    public TextSpan Span { get; }

    /// <summary>What is changed, for example "p_vccgt_spec in specification set SBF_BFUNC".</summary>
    public string Owner { get; }

    /// <summary>True when the definition holds one value shared by several selectors.</summary>
    public bool AppliesToAllSelectors { get; }

    /// <summary>Why the value cannot be edited from the table, or null when it can.</summary>
    public string? Reason { get; }

    public static ParameterEditTarget Level() => new ParameterEditTarget(PairedDocumentKind.Levels, default, "the level", false, null);

    public static ParameterEditTarget Specification(TextSpan span, string owner, bool appliesToAllSelectors)
        => new ParameterEditTarget(PairedDocumentKind.TestConditions, span, owner, appliesToAllSelectors, null);

    public static ParameterEditTarget NotEditable(string reason) => new ParameterEditTarget(PairedDocumentKind.Levels, default, string.Empty, false, reason);
}

public sealed class ParameterAudit
{
    public ParameterAudit(int references, int unresolved, IReadOnlyList<string> problems)
    {
        References = references;
        Unresolved = unresolved;
        Problems = problems;
    }

    /// <summary>Level cells that reference a parameter, counted once per test condition.</summary>
    public int References { get; }

    /// <summary>How many of those could not be resolved.</summary>
    public int Unresolved { get; }

    /// <summary>The distinct reasons, most useful for a status line or a report.</summary>
    public IReadOnlyList<string> Problems { get; }
}

/// <summary>
/// Resolves the parameters a level refers to (p_..., c_...) to values for one selector.
/// A name is looked up in the specification set of the linked test-condition group first, then in the
/// uservars. A specification value that lists one entry per selector yields the entry of the requested
/// selector. Names must match exactly (case-sensitive, never partial). Formulas built from + - * / and
/// parentheses are calculated, with unit prefixes (mV, uA, mS, ...) converted to base units.
/// </summary>
public sealed class ParameterResolver
{
    public const string UnresolvedPrefix = "UNRESOLVED: ";

    private const int MaximumDepth = 32;
    private static readonly Regex NumberToken = new Regex(@"\G(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?([A-Za-z%]*)", RegexOptions.CultureInvariant);
    private static readonly Regex NameToken = new Regex(@"\G[A-Za-z_]\w*(\.[A-Za-z_]\w*)*", RegexOptions.CultureInvariant);
    private static readonly Regex StringToken = new Regex(@"\G""(\\.|[^""\\])*""", RegexOptions.CultureInvariant);
    private static readonly Regex SingleName = new Regex(@"^[A-Za-z_]\w*(\.[A-Za-z_]\w*)*$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> BaseUnits = new HashSet<string>(StringComparer.Ordinal) { "V", "A", "S", "s", "Hz", "Ohm", "W", "F", "H" };

    private readonly UserVarsDocument userVars;
    private readonly Dictionary<SpecificationSetSyntax, Dictionary<string, TypedValueSyntax>> specificationIndexes = new Dictionary<SpecificationSetSyntax, Dictionary<string, TypedValueSyntax>>();

    public ParameterResolver(UserVarsDocument userVars)
    {
        this.userVars = userVars ?? throw new ArgumentNullException(nameof(userVars));
    }

    /// <summary>
    /// False for level properties whose value names something other than a parameter. PowerDomain holds the
    /// name of a power domain declared in the pin definitions (for example PDomainStress).
    /// </summary>
    public static bool IsParameterProperty(string propertyName)
    {
        return !string.Equals(propertyName, "PowerDomain", StringComparison.Ordinal);
    }

    /// <summary>Resolves one level expression for <paramref name="selector"/> of <paramref name="group"/>.</summary>
    public ResolvedParameter Resolve(string expression, LinkedTestConditionGroup group, string selector)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        var text = (expression ?? string.Empty).Trim();
        var scope = new Scope(group.SpecificationSet, selector);
        if (text.Length == 0) return new ResolvedParameter(text, text, false, null, scope.Steps);

        try
        {
            var value = Evaluate(text, scope, 0);
            return scope.Steps.Count == 0
                ? new ResolvedParameter(text, text, false, null, scope.Steps) // Literal: keep it exactly as written.
                : new ResolvedParameter(text, value.Format(), true, null, scope.Steps);
        }
        catch (MissingDefinitionException exception)
        {
            return new ResolvedParameter(text, text, scope.Steps.Count > 0, exception.Message, scope.Steps);
        }
        catch (EvaluationException)
        {
            // Not something this resolver understands; show it as written rather than guess.
            return new ResolvedParameter(text, text, false, null, scope.Steps);
        }
    }

    /// <summary>
    /// Finds the definition that holds the value shown for <paramref name="expression"/> and <paramref name="selector"/>.
    /// A literal in the level is edited in the levels file. A parameter is followed through the specification set
    /// to the entry that holds a literal; that entry (for the selector, when the value lists one per selector) is the
    /// target. Values calculated by a formula or taken from a uservar cannot be set from the table.
    /// </summary>
    public ParameterEditTarget FindEditTarget(string expression, LinkedTestConditionGroup group, string selector, string testConditionsText)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        if (testConditionsText == null) throw new ArgumentNullException(nameof(testConditionsText));
        var text = (expression ?? string.Empty).Trim();
        if (text.Length == 0) return ParameterEditTarget.NotEditable("The property is not set on this row; add it in the .lvl source first.");
        if (!ContainsName(text)) return ParameterEditTarget.Level();
        if (!SingleName.IsMatch(text)) return ParameterEditTarget.NotEditable("The level holds a formula; edit it on the level set itself.");

        var specification = group.SpecificationSet;
        var name = text;
        for (var depth = 0; depth <= MaximumDepth; depth++)
        {
            if (specification == null || !Index(specification).TryGetValue(name, out var value))
            {
                var separator = name.LastIndexOf('.');
                var isUserVar = userVars.FindExact(separator < 0 ? name : name.Substring(separator + 1)).Count > 0;
                return ParameterEditTarget.NotEditable(isUserVar
                    ? "The value comes from uservar '" + name + "', which is defined in the uservar file and cannot be changed here."
                    : "'" + name + "' has no exact definition.");
            }

            var span = value.ExpressionSpan;
            if (span.Start < 0 || span.End > testConditionsText.Length) return ParameterEditTarget.NotEditable("The table is out of date; reload from disk.");
            var slice = testConditionsText.Substring(span.Start, span.Length);
            if (!string.Equals(slice.Trim(), value.Expression.Trim(), StringComparison.Ordinal)) return ParameterEditTarget.NotEditable("The table is out of date; reload from disk.");

            var entries = SplitTopLevelSpans(slice);
            var entryIndex = 0;
            if (entries.Count > 1)
            {
                entryIndex = IndexOf(specification.Selectors, selector);
                if (entries.Count != specification.Selectors.Count || entryIndex < 0)
                {
                    return ParameterEditTarget.NotEditable("'" + value.Name + "' does not list one value per selector of specification set '" + specification.Name + "'.");
                }
            }

            var entry = entries[entryIndex];
            var entryText = slice.Substring(entry.Start, entry.Length);
            if (!ContainsName(entryText))
            {
                return ParameterEditTarget.Specification(
                    new TextSpan(span.Start + entry.Start, entry.Length),
                    value.Name + " in specification set " + specification.Name,
                    entries.Count == 1 && specification.Selectors.Count > 1);
            }

            if (!SingleName.IsMatch(entryText))
            {
                return ParameterEditTarget.NotEditable("The value is calculated: " + value.Name + " = " + entryText + ". Edit the parameters it uses.");
            }

            name = entryText;
        }

        return ParameterEditTarget.NotEditable("'" + text + "' refers back to itself.");
    }

    /// <summary>True when the expression refers to at least one name (TRUE and FALSE are literals).</summary>
    private static bool ContainsName(string expression)
    {
        var position = 0;
        while (position < expression.Length)
        {
            var number = NumberToken.Match(expression, position);
            if (number.Success && number.Length > 0)
            {
                position += number.Length;
                continue;
            }

            var quoted = StringToken.Match(expression, position);
            if (quoted.Success)
            {
                position += quoted.Length;
                continue;
            }

            var name = NameToken.Match(expression, position);
            if (name.Success)
            {
                if (!string.Equals(name.Value, "TRUE", StringComparison.OrdinalIgnoreCase) && !string.Equals(name.Value, "FALSE", StringComparison.OrdinalIgnoreCase)) return true;
                position += name.Length;
                continue;
            }

            position++;
        }
        return false;
    }

    /// <summary>Positions of the comma-separated entries of a specification value, trimmed, ignoring commas in parentheses or strings.</summary>
    private static List<TextSpan> SplitTopLevelSpans(string expression)
    {
        var spans = new List<TextSpan>();
        var start = 0;
        var depth = 0;
        var quoted = false;
        for (var index = 0; index <= expression.Length; index++)
        {
            var atEnd = index == expression.Length;
            if (!atEnd)
            {
                var current = expression[index];
                if (current == '"' && (index == 0 || expression[index - 1] != '\\')) quoted = !quoted;
                if (quoted) continue;
                if (current == '(') depth++;
                if (current == ')') depth--;
                if (current != ',' || depth != 0) continue;
            }

            var entryStart = start;
            var entryEnd = index;
            while (entryStart < entryEnd && char.IsWhiteSpace(expression[entryStart])) entryStart++;
            while (entryEnd > entryStart && char.IsWhiteSpace(expression[entryEnd - 1])) entryEnd--;
            spans.Add(new TextSpan(entryStart, entryEnd - entryStart));
            start = index + 1;
        }
        return spans;
    }

    /// <summary>Resolves every parameter reference of every level for every test condition.</summary>
    public static ParameterAudit Audit(CombinedLevelsModel model, UserVarsDocument userVars)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        var resolver = new ParameterResolver(userVars);
        var references = 0;
        var unresolved = 0;
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var level in model.LevelSets)
        {
            foreach (var group in level.Groups)
            {
                foreach (var condition in group.TestConditions)
                {
                    foreach (var entry in level.LevelSet.Entries)
                    {
                        if (!(entry is ResourceEntrySyntax resource)) continue;
                        foreach (var property in resource.Properties)
                        {
                            if (!IsParameterProperty(property.Name)) continue;
                            var resolved = resolver.Resolve(property.Expression, group, condition.Selector);
                            if (!resolved.Changed && resolved.Problem == null) continue;
                            references++;
                            if (resolved.Problem == null) continue;
                            unresolved++;
                            if (seen.Add(resolved.Problem)) problems.Add(resolved.Problem);
                        }
                    }
                }
            }
        }

        return new ParameterAudit(references, unresolved, problems);
    }

    private Value Evaluate(string expression, Scope scope, int depth)
    {
        if (depth > MaximumDepth) throw new MissingDefinitionException("'" + expression + "' refers back to itself");
        var parser = new ExpressionParser(expression, name => ResolveName(name, scope, depth));
        return parser.Parse();
    }

    private Value ResolveName(string name, Scope scope, int depth)
    {
        if (string.Equals(name, "TRUE", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return Value.FromText(name);
        }

        if (scope.Specification != null && Index(scope.Specification).TryGetValue(name, out var specificationValue))
        {
            var entries = SemanticLinker.SplitTopLevelValues(specificationValue.Expression);
            string chosen;
            if (entries.Count == 1)
            {
                chosen = entries[0];
                scope.AddStep(name + " = " + chosen);
            }
            else
            {
                var selectorIndex = IndexOf(scope.Specification.Selectors, scope.Selector);
                if (entries.Count != scope.Specification.Selectors.Count || selectorIndex < 0)
                {
                    throw new MissingDefinitionException(
                        "'" + name + "' lists " + entries.Count + " values but specification set '" + scope.Specification.Name + "' has "
                        + scope.Specification.Selectors.Count + " selectors" + (selectorIndex < 0 ? " and none is named exactly '" + scope.Selector + "'" : string.Empty));
                }
                chosen = entries[selectorIndex];
                scope.AddStep(name + " = " + chosen + "   [" + scope.Selector + ", entry " + (selectorIndex + 1) + " of " + entries.Count + "]");
            }

            return Evaluate(chosen, scope, depth + 1);
        }

        var separator = name.LastIndexOf('.');
        var collection = separator < 0 ? null : name.Substring(0, separator);
        var shortName = separator < 0 ? name : name.Substring(separator + 1);
        UserVarDefinition? definition = null;
        var matches = 0;
        foreach (var candidate in userVars.FindExact(shortName))
        {
            if (collection != null && !string.Equals(candidate.Collection, collection, StringComparison.Ordinal)) continue;
            definition ??= candidate;
            matches++;
        }

        if (matches > 1) throw new MissingDefinitionException("uservar '" + name + "' is defined more than once");
        if (definition == null)
        {
            var message = "no parameter or uservar is named exactly '" + name + "'";
            foreach (var near in userVars.FindIgnoringCase(shortName))
            {
                message += " ('" + near.Name + "' differs only in letter case)";
                break;
            }
            throw new MissingDefinitionException(message);
        }

        scope.AddStep(definition.Name + " = " + definition.Expression + (definition.Collection.Length > 0 ? "   [uservar, collection " + definition.Collection + "]" : "   [uservar]"));
        try
        {
            // A uservar is defined outside any specification set, so only other uservars are in reach.
            return Evaluate(definition.Expression, scope.WithoutSpecification(), depth + 1);
        }
        catch (EvaluationException exception) when (!(exception is MissingDefinitionException))
        {
            return Value.FromText(definition.Expression);
        }
    }

    private Dictionary<string, TypedValueSyntax> Index(SpecificationSetSyntax specification)
    {
        if (!specificationIndexes.TryGetValue(specification, out var index))
        {
            index = new Dictionary<string, TypedValueSyntax>(StringComparer.Ordinal);
            foreach (var value in specification.Values)
            {
                if (!index.ContainsKey(value.Name)) index.Add(value.Name, value);
            }
            specificationIndexes.Add(specification, index);
        }
        return index;
    }

    private static int IndexOf(IReadOnlyList<string> items, string item)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (string.Equals(items[index], item, StringComparison.Ordinal)) return index;
        }
        return -1;
    }

    private sealed class Scope
    {
        private readonly List<string> steps;
        private readonly HashSet<string> seenSteps;

        public Scope(SpecificationSetSyntax? specification, string selector)
            : this(specification, selector, new List<string>(), new HashSet<string>(StringComparer.Ordinal))
        {
        }

        private Scope(SpecificationSetSyntax? specification, string selector, List<string> steps, HashSet<string> seenSteps)
        {
            Specification = specification;
            Selector = selector ?? string.Empty;
            this.steps = steps;
            this.seenSteps = seenSteps;
        }

        public SpecificationSetSyntax? Specification { get; }
        public string Selector { get; }
        public IReadOnlyList<string> Steps => steps;

        public void AddStep(string step)
        {
            if (seenSteps.Add(step)) steps.Add(step);
        }

        public Scope WithoutSpecification() => new Scope(null, Selector, steps, seenSteps);
    }

    private class EvaluationException : Exception
    {
        public EvaluationException(string message)
            : base(message)
        {
        }
    }

    private sealed class MissingDefinitionException : EvaluationException
    {
        public MissingDefinitionException(string message)
            : base(message)
        {
        }
    }

    private readonly struct Value
    {
        private Value(bool isNumber, double number, string unit, string text)
        {
            IsNumber = isNumber;
            Number = number;
            Unit = unit;
            Text = text;
        }

        public bool IsNumber { get; }
        public double Number { get; }
        public string Unit { get; }

        /// <summary>For a number: its literal spelling while untouched by arithmetic. Otherwise the text value.</summary>
        public string Text { get; }

        public static Value FromText(string text) => new Value(false, 0, string.Empty, text);

        public static Value FromLiteral(double number, string unit, string spelling) => new Value(true, number, unit, spelling);

        public static Value FromNumber(double number, string unit) => new Value(true, number, unit, string.Empty);

        public string Format()
        {
            if (!IsNumber || Text.Length > 0) return Text;
            return Number.ToString("G10", CultureInfo.InvariantCulture) + Unit;
        }
    }

    private sealed class ExpressionParser
    {
        private readonly string text;
        private readonly Func<string, Value> resolveName;
        private int position;

        public ExpressionParser(string text, Func<string, Value> resolveName)
        {
            this.text = text;
            this.resolveName = resolveName;
        }

        public Value Parse()
        {
            var value = ParseSum();
            SkipWhitespace();
            if (position < text.Length) throw new EvaluationException("Unexpected '" + text[position] + "' in '" + text + "'.");
            return value;
        }

        private Value ParseSum()
        {
            var left = ParseProduct();
            while (true)
            {
                SkipWhitespace();
                if (position >= text.Length || (text[position] != '+' && text[position] != '-')) return left;
                var subtract = text[position] == '-';
                position++;
                var right = ParseProduct();
                RequireNumbers(left, right);
                var unit = left.Unit.Length > 0 ? left.Unit : right.Unit;
                left = Value.FromNumber(subtract ? left.Number - right.Number : left.Number + right.Number, unit);
            }
        }

        private Value ParseProduct()
        {
            var left = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (position >= text.Length || (text[position] != '*' && text[position] != '/')) return left;
                var divide = text[position] == '/';
                position++;
                var right = ParseUnary();
                RequireNumbers(left, right);
                string unit;
                if (right.Unit.Length == 0) unit = left.Unit;
                else if (left.Unit.Length == 0) unit = divide ? "/" + right.Unit : right.Unit;
                else if (divide && string.Equals(left.Unit, right.Unit, StringComparison.Ordinal)) unit = string.Empty;
                else unit = left.Unit + (divide ? "/" : "*") + right.Unit;
                left = Value.FromNumber(divide ? left.Number / right.Number : left.Number * right.Number, unit);
            }
        }

        private Value ParseUnary()
        {
            SkipWhitespace();
            if (position < text.Length && (text[position] == '-' || text[position] == '+'))
            {
                var negate = text[position] == '-';
                var start = position;
                position++;
                var operand = ParseUnary();
                if (!operand.IsNumber) throw new EvaluationException("A sign needs a number in '" + text + "'.");
                if (!negate) return operand;
                // A signed literal such as -5.00A keeps its spelling.
                return operand.Text.Length > 0
                    ? Value.FromLiteral(-operand.Number, operand.Unit, text.Substring(start, position - start).Trim())
                    : Value.FromNumber(-operand.Number, operand.Unit);
            }
            return ParsePrimary();
        }

        private Value ParsePrimary()
        {
            SkipWhitespace();
            if (position >= text.Length) throw new EvaluationException("'" + text + "' ends unexpectedly.");

            if (text[position] == '(')
            {
                position++;
                var inner = ParseSum();
                SkipWhitespace();
                if (position >= text.Length || text[position] != ')') throw new EvaluationException("Missing ')' in '" + text + "'.");
                position++;
                return inner.IsNumber ? Value.FromNumber(inner.Number, inner.Unit) : inner;
            }

            var number = NumberToken.Match(text, position);
            if (number.Success)
            {
                position += number.Length;
                var magnitude = double.Parse(number.Groups[1].Value + number.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                var unit = SplitUnit(number.Groups[3].Value, out var scale);
                return Value.FromLiteral(magnitude * scale, unit, number.Value);
            }

            var quoted = StringToken.Match(text, position);
            if (quoted.Success)
            {
                position += quoted.Length;
                return Value.FromText(quoted.Value);
            }

            var name = NameToken.Match(text, position);
            if (name.Success)
            {
                position += name.Length;
                return resolveName(name.Value);
            }

            throw new EvaluationException("Unexpected '" + text[position] + "' in '" + text + "'.");
        }

        private static void RequireNumbers(Value left, Value right)
        {
            if (!left.IsNumber || !right.IsNumber) throw new EvaluationException("Arithmetic needs numbers.");
        }

        /// <summary>Splits a unit suffix such as mV into its base unit and scale. Unknown suffixes are kept as written.</summary>
        private static string SplitUnit(string suffix, out double scale)
        {
            scale = 1;
            if (suffix.Length == 0 || BaseUnits.Contains(suffix)) return suffix;
            if (suffix.Length > 1 && BaseUnits.Contains(suffix.Substring(1)))
            {
                switch (suffix[0])
                {
                    case 'f': scale = 1e-15; break;
                    case 'p': scale = 1e-12; break;
                    case 'n': scale = 1e-9; break;
                    case 'u': scale = 1e-6; break;
                    case 'm': scale = 1e-3; break;
                    case 'k':
                    case 'K': scale = 1e3; break;
                    case 'M': scale = 1e6; break;
                    case 'G': scale = 1e9; break;
                    default: return suffix;
                }
                return suffix.Substring(1);
            }
            return suffix;
        }

        private void SkipWhitespace()
        {
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        }
    }
}
