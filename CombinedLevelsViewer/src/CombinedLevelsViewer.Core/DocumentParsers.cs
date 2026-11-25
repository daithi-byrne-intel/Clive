using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CombinedLevelsViewer.Core;

public static class LevelsParser
{
    private static readonly Regex VersionPattern = new Regex(@"\bVersion\s+([^;]+);", RegexOptions.Compiled);
    private static readonly Regex DeclarationPattern = new Regex(@"(?m)^[ \t]*Levels[ \t]+([A-Za-z_][A-Za-z0-9_]*)[ \t\r\n]*\{", RegexOptions.Compiled);

    public static LevelsDocument Parse(string text)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));

        var diagnostics = new List<DocumentDiagnostic>();
        var levelSets = new List<LevelSetSyntax>();
        var versionMatch = VersionPattern.Match(text);
        var version = versionMatch.Success ? versionMatch.Groups[1].Value.Trim() : string.Empty;
        if (!versionMatch.Success)
        {
            diagnostics.Add(new DocumentDiagnostic("Missing Version declaration.", new TextSpan(0, 0), DiagnosticSeverity.Warning));
        }

        foreach (Match match in DeclarationPattern.Matches(text))
        {
            var openBrace = text.IndexOf('{', match.Index + match.Length - 1);
            var closeBrace = ParserUtilities.FindMatchingBrace(text, openBrace);
            if (closeBrace < 0)
            {
                diagnostics.Add(new DocumentDiagnostic("Unclosed Levels declaration.", new TextSpan(match.Index, match.Length)));
                continue;
            }

            var entries = ParseEntries(text, openBrace + 1, closeBrace, diagnostics);
            levelSets.Add(new LevelSetSyntax(match.Groups[1].Value, new TextSpan(match.Index, closeBrace - match.Index + 1), entries));
        }

        if (levelSets.Count == 0)
        {
            diagnostics.Add(new DocumentDiagnostic("No Levels declarations were found.", new TextSpan(0, text.Length)));
        }

        return new LevelsDocument(text, version, levelSets, diagnostics);
    }

    private static IReadOnlyList<LevelEntrySyntax> ParseEntries(string text, int start, int end, List<DocumentDiagnostic> diagnostics)
    {
        var entries = new List<LevelEntrySyntax>();
        var position = start;
        while (position < end)
        {
            ParserUtilities.SkipTrivia(text, ref position, end);
            if (position >= end) break;

            var entryStart = position;
            var name = ParserUtilities.ReadIdentifier(text, ref position, end);
            if (name.Length == 0)
            {
                diagnostics.Add(new DocumentDiagnostic("Expected a resource name or SequenceBreak.", new TextSpan(position, 1), DiagnosticSeverity.Warning));
                position++;
                continue;
            }

            ParserUtilities.SkipTrivia(text, ref position, end);
            if (string.Equals(name, "SequenceBreak", StringComparison.Ordinal))
            {
                var expressionSpan = ParserUtilities.ReadExpression(text, ref position, end, diagnostics);
                entries.Add(new SequenceBreakSyntax(
                    text.Substring(expressionSpan.Start, expressionSpan.Length).Trim(),
                    ParserUtilities.TrimSpan(text, expressionSpan),
                    new TextSpan(entryStart, position - entryStart)));
                continue;
            }

            if (position >= end || text[position] != '{')
            {
                diagnostics.Add(new DocumentDiagnostic("Expected '{' after resource name '" + name + "'.", new TextSpan(entryStart, position - entryStart)));
                ParserUtilities.AdvanceToStatementEnd(text, ref position, end);
                continue;
            }

            var closeBrace = ParserUtilities.FindMatchingBrace(text, position);
            if (closeBrace < 0 || closeBrace > end)
            {
                diagnostics.Add(new DocumentDiagnostic("Unclosed resource block '" + name + "'.", new TextSpan(entryStart, end - entryStart)));
                break;
            }

            var properties = ParserUtilities.ParseAssignments(text, position + 1, closeBrace, diagnostics);
            position = closeBrace + 1;
            entries.Add(new ResourceEntrySyntax(name, new TextSpan(entryStart, position - entryStart), properties));
        }

        return entries;
    }
}

public static class TcgParser
{
    private static readonly Regex VersionPattern = new Regex(@"\bVersion\s+([^;]+);", RegexOptions.Compiled);
    private static readonly Regex DeclarationPattern = new Regex(@"(?m)^[ \t]*(SpecificationSet|TestConditionGroup|TestCondition)[ \t]+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    public static TcgDocument Parse(string text)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));

        var diagnostics = new List<DocumentDiagnostic>();
        var specificationSets = new List<SpecificationSetSyntax>();
        var groups = new List<TestConditionGroupSyntax>();
        var testConditions = new List<TestConditionSyntax>();
        var versionMatch = VersionPattern.Match(text);
        var version = versionMatch.Success ? versionMatch.Groups[1].Value.Trim() : string.Empty;

        foreach (Match match in DeclarationPattern.Matches(text))
        {
            var kind = match.Groups[1].Value;
            var name = match.Groups[2].Value;
            var openBrace = ParserUtilities.FindOpeningBrace(text, match.Index + match.Length);
            if (openBrace < 0)
            {
                diagnostics.Add(new DocumentDiagnostic("Missing body for " + kind + " '" + name + "'.", new TextSpan(match.Index, match.Length)));
                continue;
            }

            var closeBrace = ParserUtilities.FindMatchingBrace(text, openBrace);
            if (closeBrace < 0)
            {
                diagnostics.Add(new DocumentDiagnostic("Unclosed " + kind + " declaration '" + name + "'.", new TextSpan(match.Index, match.Length)));
                continue;
            }

            var declarationSpan = new TextSpan(match.Index, closeBrace - match.Index + 1);
            if (kind == "SpecificationSet")
            {
                var selectors = ParseSelectors(text, match.Index + match.Length, openBrace);
                var values = ParserUtilities.ParseTypedValues(text, openBrace + 1, closeBrace, diagnostics);
                specificationSets.Add(new SpecificationSetSyntax(name, selectors, declarationSpan, values));
            }
            else
            {
                var assignments = ParserUtilities.ParseAssignments(text, openBrace + 1, closeBrace, diagnostics);
                if (kind == "TestConditionGroup")
                {
                    groups.Add(new TestConditionGroupSyntax(
                        name,
                        FindValue(assignments, "SpecificationSet"),
                        FindValue(assignments, "Level"),
                        declarationSpan));
                }
                else
                {
                    testConditions.Add(new TestConditionSyntax(
                        name,
                        FindValue(assignments, "TestConditionGroup"),
                        FindValue(assignments, "Selector"),
                        declarationSpan));
                }
            }
        }

        if (!versionMatch.Success)
        {
            diagnostics.Add(new DocumentDiagnostic("Missing Version declaration.", new TextSpan(0, 0), DiagnosticSeverity.Warning));
        }

        return new TcgDocument(text, version, specificationSets, groups, testConditions, diagnostics);
    }

    private static IReadOnlyList<string> ParseSelectors(string text, int start, int end)
    {
        var selectors = new List<string>();
        var open = text.IndexOf('(', start, end - start);
        if (open < 0) return selectors;
        var close = text.IndexOf(')', open + 1, end - open - 1);
        if (close < 0) return selectors;

        foreach (var selector in text.Substring(open + 1, close - open - 1).Split(','))
        {
            var value = selector.Trim();
            if (value.Length > 0) selectors.Add(value);
        }

        return selectors;
    }

    private static string FindValue(IReadOnlyList<PropertyAssignmentSyntax> assignments, string name)
    {
        foreach (var assignment in assignments)
        {
            if (string.Equals(assignment.Name, name, StringComparison.Ordinal)) return assignment.Expression.Trim();
        }

        return string.Empty;
    }
}

internal static class ParserUtilities
{
    public static void SkipTrivia(string text, ref int position, int end)
    {
        while (position < end)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
                continue;
            }

            if (text[position] == '#')
            {
                while (position < end && text[position] != '\n') position++;
                continue;
            }

            break;
        }
    }

    public static string ReadIdentifier(string text, ref int position, int end)
    {
        var start = position;
        if (position >= end || !(char.IsLetter(text[position]) || text[position] == '_')) return string.Empty;
        position++;
        while (position < end && (char.IsLetterOrDigit(text[position]) || text[position] == '_')) position++;
        return text.Substring(start, position - start);
    }

    public static int FindOpeningBrace(string text, int start)
    {
        var quoted = false;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '"' && (i == 0 || text[i - 1] != '\\')) quoted = !quoted;
            if (!quoted && text[i] == '#')
            {
                while (i < text.Length && text[i] != '\n') i++;
            }
            else if (!quoted && text[i] == '{') return i;
            else if (!quoted && text[i] == ';') return -1;
        }

        return -1;
    }

    public static int FindMatchingBrace(string text, int openBrace)
    {
        if (openBrace < 0 || openBrace >= text.Length || text[openBrace] != '{') return -1;
        var depth = 0;
        var quoted = false;
        for (var i = openBrace; i < text.Length; i++)
        {
            var current = text[i];
            if (current == '"' && (i == 0 || text[i - 1] != '\\')) quoted = !quoted;
            if (quoted) continue;
            if (current == '#')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (current == '{') depth++;
            if (current == '}' && --depth == 0) return i;
        }

        return -1;
    }

    public static TextSpan ReadExpression(string text, ref int position, int end, List<DocumentDiagnostic> diagnostics)
    {
        var start = position;
        var quoted = false;
        var parentheses = 0;
        while (position < end)
        {
            var current = text[position];
            if (current == '"' && (position == 0 || text[position - 1] != '\\')) quoted = !quoted;
            if (!quoted)
            {
                if (current == '(') parentheses++;
                if (current == ')') parentheses--;
                if (current == ';' && parentheses <= 0)
                {
                    var span = new TextSpan(start, position - start);
                    position++;
                    return span;
                }
            }
            position++;
        }

        diagnostics.Add(new DocumentDiagnostic("Expected ';' after expression.", new TextSpan(start, Math.Max(0, end - start))));
        return new TextSpan(start, Math.Max(0, end - start));
    }

    public static IReadOnlyList<PropertyAssignmentSyntax> ParseAssignments(string text, int start, int end, List<DocumentDiagnostic> diagnostics)
    {
        var assignments = new List<PropertyAssignmentSyntax>();
        var position = start;
        while (position < end)
        {
            SkipTrivia(text, ref position, end);
            if (position >= end) break;
            var assignmentStart = position;
            var name = ReadIdentifier(text, ref position, end);
            SkipTrivia(text, ref position, end);
            if (name.Length == 0 || position >= end || text[position] != '=')
            {
                diagnostics.Add(new DocumentDiagnostic("Expected an assignment.", new TextSpan(assignmentStart, Math.Max(1, position - assignmentStart)), DiagnosticSeverity.Warning));
                AdvanceToStatementEnd(text, ref position, end);
                continue;
            }

            position++;
            var expressionSpan = TrimSpan(text, ReadExpression(text, ref position, end, diagnostics));
            assignments.Add(new PropertyAssignmentSyntax(name, text.Substring(expressionSpan.Start, expressionSpan.Length), expressionSpan, new TextSpan(assignmentStart, position - assignmentStart)));
        }

        return assignments;
    }

    public static IReadOnlyList<TypedValueSyntax> ParseTypedValues(string text, int start, int end, List<DocumentDiagnostic> diagnostics)
    {
        var values = new List<TypedValueSyntax>();
        var position = start;
        while (position < end)
        {
            SkipTrivia(text, ref position, end);
            if (position >= end) break;
            var statementStart = position;
            var type = ReadIdentifier(text, ref position, end);
            SkipTrivia(text, ref position, end);
            var name = ReadIdentifier(text, ref position, end);
            SkipTrivia(text, ref position, end);
            if (type.Length == 0 || name.Length == 0 || position >= end || text[position] != '=')
            {
                diagnostics.Add(new DocumentDiagnostic("Expected a typed specification value.", new TextSpan(statementStart, Math.Max(1, position - statementStart)), DiagnosticSeverity.Warning));
                AdvanceToStatementEnd(text, ref position, end);
                continue;
            }

            position++;
            var expressionSpan = TrimSpan(text, ReadExpression(text, ref position, end, diagnostics));
            values.Add(new TypedValueSyntax(type, name, text.Substring(expressionSpan.Start, expressionSpan.Length), expressionSpan, new TextSpan(statementStart, position - statementStart)));
        }

        return values;
    }

    public static TextSpan TrimSpan(string text, TextSpan span)
    {
        var start = span.Start;
        var end = span.End;
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        return new TextSpan(start, end - start);
    }

    public static void AdvanceToStatementEnd(string text, ref int position, int end)
    {
        while (position < end && text[position] != ';' && text[position] != '\n') position++;
        if (position < end) position++;
    }
}
