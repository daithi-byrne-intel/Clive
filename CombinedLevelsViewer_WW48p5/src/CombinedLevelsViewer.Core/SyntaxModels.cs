using System;
using System.Collections.Generic;

namespace CombinedLevelsViewer.Core;

public readonly struct TextSpan
{
    public TextSpan(int start, int length)
    {
        Start = start;
        Length = length;
    }

    public int Start { get; }
    public int Length { get; }
    public int End => Start + Length;
}

public enum DiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public sealed class DocumentDiagnostic
{
    public DocumentDiagnostic(string message, TextSpan span, DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        Message = message;
        Span = span;
        Severity = severity;
    }

    public string Message { get; }
    public TextSpan Span { get; }
    public DiagnosticSeverity Severity { get; }
}

public abstract class SourceDocument
{
    protected SourceDocument(string text, string version, IReadOnlyList<DocumentDiagnostic> diagnostics)
    {
        Text = text;
        Version = version;
        Diagnostics = diagnostics;
    }

    public string Text { get; }
    public string Version { get; }
    public IReadOnlyList<DocumentDiagnostic> Diagnostics { get; }
    public bool HasErrors
    {
        get
        {
            foreach (var diagnostic in Diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

public sealed class LevelsDocument : SourceDocument
{
    public LevelsDocument(string text, string version, IReadOnlyList<LevelSetSyntax> levelSets, IReadOnlyList<DocumentDiagnostic> diagnostics)
        : base(text, version, diagnostics)
    {
        LevelSets = levelSets;
    }

    public IReadOnlyList<LevelSetSyntax> LevelSets { get; }
}

public sealed class LevelSetSyntax
{
    public LevelSetSyntax(string name, TextSpan span, IReadOnlyList<LevelEntrySyntax> entries)
    {
        Name = name;
        Span = span;
        Entries = entries;
    }

    public string Name { get; }
    public TextSpan Span { get; }
    public IReadOnlyList<LevelEntrySyntax> Entries { get; }
}

public abstract class LevelEntrySyntax
{
    protected LevelEntrySyntax(TextSpan span)
    {
        Span = span;
    }

    public TextSpan Span { get; }
}

public sealed class ResourceEntrySyntax : LevelEntrySyntax
{
    public ResourceEntrySyntax(string name, TextSpan span, IReadOnlyList<PropertyAssignmentSyntax> properties)
        : base(span)
    {
        Name = name;
        Properties = properties;
    }

    public string Name { get; }
    public IReadOnlyList<PropertyAssignmentSyntax> Properties { get; }
}

public sealed class SequenceBreakSyntax : LevelEntrySyntax
{
    public SequenceBreakSyntax(string expression, TextSpan expressionSpan, TextSpan span)
        : base(span)
    {
        Expression = expression;
        ExpressionSpan = expressionSpan;
    }

    public string Expression { get; }
    public TextSpan ExpressionSpan { get; }
}

public sealed class PropertyAssignmentSyntax
{
    public PropertyAssignmentSyntax(string name, string expression, TextSpan expressionSpan, TextSpan span)
    {
        Name = name;
        Expression = expression;
        ExpressionSpan = expressionSpan;
        Span = span;
    }

    public string Name { get; }
    public string Expression { get; }
    public TextSpan ExpressionSpan { get; }
    public TextSpan Span { get; }
}

public sealed class TcgDocument : SourceDocument
{
    public TcgDocument(
        string text,
        string version,
        IReadOnlyList<SpecificationSetSyntax> specificationSets,
        IReadOnlyList<TestConditionGroupSyntax> groups,
        IReadOnlyList<TestConditionSyntax> testConditions,
        IReadOnlyList<DocumentDiagnostic> diagnostics)
        : base(text, version, diagnostics)
    {
        SpecificationSets = specificationSets;
        Groups = groups;
        TestConditions = testConditions;
    }

    public IReadOnlyList<SpecificationSetSyntax> SpecificationSets { get; }
    public IReadOnlyList<TestConditionGroupSyntax> Groups { get; }
    public IReadOnlyList<TestConditionSyntax> TestConditions { get; }
}

public sealed class SpecificationSetSyntax
{
    public SpecificationSetSyntax(string name, IReadOnlyList<string> selectors, TextSpan span, IReadOnlyList<TypedValueSyntax> values)
    {
        Name = name;
        Selectors = selectors;
        Span = span;
        Values = values;
    }

    public string Name { get; }
    public IReadOnlyList<string> Selectors { get; }
    public TextSpan Span { get; }
    public IReadOnlyList<TypedValueSyntax> Values { get; }
}

public sealed class TypedValueSyntax
{
    public TypedValueSyntax(string type, string name, string expression, TextSpan expressionSpan, TextSpan span)
    {
        Type = type;
        Name = name;
        Expression = expression;
        ExpressionSpan = expressionSpan;
        Span = span;
    }

    public string Type { get; }
    public string Name { get; }
    public string Expression { get; }
    public TextSpan ExpressionSpan { get; }
    public TextSpan Span { get; }
}

public sealed class TestConditionGroupSyntax
{
    public TestConditionGroupSyntax(string name, string specificationSetName, string levelReference, TextSpan span)
    {
        Name = name;
        SpecificationSetName = specificationSetName;
        LevelReference = levelReference;
        Span = span;
    }

    public string Name { get; }
    public string SpecificationSetName { get; }
    public string LevelReference { get; }
    public string LevelName
    {
        get
        {
            var separator = LevelReference.LastIndexOf("::", StringComparison.Ordinal);
            return separator < 0 ? LevelReference : LevelReference.Substring(separator + 2);
        }
    }

    public TextSpan Span { get; }
}

public sealed class TestConditionSyntax
{
    public TestConditionSyntax(string name, string groupName, string selector, TextSpan span)
    {
        Name = name;
        GroupName = groupName;
        Selector = selector;
        Span = span;
    }

    public string Name { get; }
    public string GroupName { get; }
    public string Selector { get; }
    public TextSpan Span { get; }
}
