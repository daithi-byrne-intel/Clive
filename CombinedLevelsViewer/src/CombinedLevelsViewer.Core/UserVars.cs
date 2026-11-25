using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CombinedLevelsViewer.Core;

/// <summary>One "Type name = expression;" entry of a UserVars collection in a .usrv file.</summary>
public sealed class UserVarDefinition
{
    public UserVarDefinition(string collection, string type, string name, string expression, TextSpan span)
    {
        Collection = collection;
        Type = type;
        Name = name;
        Expression = expression;
        Span = span;
    }

    public string Collection { get; }
    public string Type { get; }
    public string Name { get; }
    public string Expression { get; }
    public TextSpan Span { get; }
}

public sealed class UserVarsDocument
{
    private readonly Dictionary<string, List<UserVarDefinition>> byName = new Dictionary<string, List<UserVarDefinition>>(StringComparer.Ordinal);
    private readonly Dictionary<string, List<UserVarDefinition>> byNameIgnoringCase = new Dictionary<string, List<UserVarDefinition>>(StringComparer.OrdinalIgnoreCase);

    public UserVarsDocument(IReadOnlyList<UserVarDefinition> definitions)
    {
        Definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        foreach (var definition in definitions)
        {
            Add(byName, definition);
            Add(byNameIgnoringCase, definition);
        }
    }

    public static UserVarsDocument Empty { get; } = new UserVarsDocument(new List<UserVarDefinition>());

    public IReadOnlyList<UserVarDefinition> Definitions { get; }

    /// <summary>Definitions whose name is exactly <paramref name="name"/> (case-sensitive, no partial matches).</summary>
    public IReadOnlyList<UserVarDefinition> FindExact(string name)
    {
        return byName.TryGetValue(name, out var found) ? found : (IReadOnlyList<UserVarDefinition>)Array.Empty<UserVarDefinition>();
    }

    /// <summary>Definitions that match only when letter case is ignored. Used to explain a failed exact match.</summary>
    public IReadOnlyList<UserVarDefinition> FindIgnoringCase(string name)
    {
        return byNameIgnoringCase.TryGetValue(name, out var found) ? found : (IReadOnlyList<UserVarDefinition>)Array.Empty<UserVarDefinition>();
    }

    private static void Add(Dictionary<string, List<UserVarDefinition>> index, UserVarDefinition definition)
    {
        if (!index.TryGetValue(definition.Name, out var list))
        {
            list = new List<UserVarDefinition>();
            index.Add(definition.Name, list);
        }
        list.Add(definition);
    }
}

/// <summary>
/// Read-only parser for .usrv files. Only "UserVars &lt;collection&gt; { Type name = expression; }" blocks are
/// read; every other declaration (Version, SelectorRuleCollection, ...) is skipped.
/// </summary>
public static class UserVarsParser
{
    private const string UserVarsKeyword = "UserVars";
    private static readonly Regex StatementPattern = new Regex(@"^\s*([A-Za-z_]\w*)\s+([A-Za-z_]\w*)\s*=\s*(.*?)\s*$", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static UserVarsDocument Parse(string text)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        var definitions = new List<UserVarDefinition>();
        var position = 0;
        while (true)
        {
            SkipTrivia(text, ref position);
            if (position >= text.Length) break;

            var word = ReadWord(text, ref position);
            if (word.Length == 0)
            {
                position++;
                continue;
            }

            if (string.Equals(word, UserVarsKeyword, StringComparison.Ordinal))
            {
                SkipTrivia(text, ref position);
                var collection = ReadWord(text, ref position);
                SkipTrivia(text, ref position);
                if (position < text.Length && text[position] == '{')
                {
                    position++;
                    ParseCollection(text, ref position, collection, definitions);
                }
            }
            else
            {
                SkipDeclaration(text, ref position);
            }
        }

        return new UserVarsDocument(definitions);
    }

    private static void ParseCollection(string text, ref int position, string collection, List<UserVarDefinition> definitions)
    {
        while (true)
        {
            SkipTrivia(text, ref position);
            if (position >= text.Length) return;
            if (text[position] == '}')
            {
                position++;
                return;
            }

            var start = position;
            var statement = new StringBuilder();
            var depth = 0;
            var terminated = false;
            while (position < text.Length)
            {
                var current = text[position];
                if (current == '#')
                {
                    SkipComment(text, ref position);
                    continue;
                }
                if (current == '"')
                {
                    var stringStart = position;
                    SkipString(text, ref position);
                    statement.Append(text, stringStart, position - stringStart);
                    continue;
                }
                if (current == '{') depth++;
                if (current == '}')
                {
                    if (depth == 0) break; // Unterminated statement; the caller closes the collection.
                    depth--;
                }
                if (current == ';' && depth == 0)
                {
                    position++;
                    terminated = true;
                    break;
                }
                statement.Append(current);
                position++;
            }

            if (!terminated) continue;
            var match = StatementPattern.Match(statement.ToString());
            if (match.Success)
            {
                definitions.Add(new UserVarDefinition(collection, match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, new TextSpan(start, position - start)));
            }
        }
    }

    /// <summary>Skips a declaration that is not a UserVars collection: either "...;" or "... { balanced }".</summary>
    private static void SkipDeclaration(string text, ref int position)
    {
        var depth = 0;
        while (position < text.Length)
        {
            var current = text[position];
            if (current == '#')
            {
                SkipComment(text, ref position);
                continue;
            }
            if (current == '"')
            {
                SkipString(text, ref position);
                continue;
            }

            position++;
            if (current == '{') depth++;
            else if (current == '}')
            {
                depth--;
                if (depth <= 0) return;
            }
            else if (current == ';' && depth == 0) return;
        }
    }

    private static void SkipTrivia(string text, ref int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position])) position++;
            else if (text[position] == '#') SkipComment(text, ref position);
            else return;
        }
    }

    private static void SkipComment(string text, ref int position)
    {
        while (position < text.Length && text[position] != '\n') position++;
    }

    private static void SkipString(string text, ref int position)
    {
        position++;
        while (position < text.Length)
        {
            if (text[position] == '\\' && position + 1 < text.Length)
            {
                position += 2;
                continue;
            }
            if (text[position] == '"')
            {
                position++;
                return;
            }
            position++;
        }
    }

    private static string ReadWord(string text, ref int position)
    {
        var start = position;
        while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_')) position++;
        return text.Substring(start, position - start);
    }
}
