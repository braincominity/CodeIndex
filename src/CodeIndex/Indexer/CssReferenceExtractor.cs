using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class CssReferenceExtractor
{
    private readonly record struct ReferencePattern(Regex Regex, string Kind, bool SkipVariableDeclarations = false);

    private static readonly Regex ScssVariableReferenceRegex = new(
        @"(?<![\w$])\$(?<name>[A-Za-z_][\w-]*)",
        RegexOptions.Compiled);

    private static readonly Regex ScssExtendReferenceRegex = new(
        @"@extend\s+(?<name>[%.][A-Za-z_][\w-]*)",
        RegexOptions.Compiled);

    private static readonly Regex CssCustomPropertyReferenceRegex = new(@"\bvar\(\s*--(?<name>[\w-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CssAnimationNameReferenceRegex = new(@"\banimation-name\s*:\s*(?<name>[\w-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CssAnimationShorthandValueRegex = new(@"\banimation\s*:\s*(?<value>[^;{}]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CssClassSelectorReferenceRegex = new(@"(?<![A-Za-z0-9_-])\.(?<name>[\w-]+)", RegexOptions.Compiled);

    private static readonly ReferencePattern[] CssReferencePatterns =
    [
        new(CssCustomPropertyReferenceRegex, "reference"),
        new(CssAnimationNameReferenceRegex, "reference"),
    ];

    private static readonly ReferencePattern[] ScssReferencePatterns =
    [
        new(ScssVariableReferenceRegex, "call", SkipVariableDeclarations: true),
        new(ScssExtendReferenceRegex, "call"),
    ];

    private static readonly HashSet<string> CssAnimationShorthandIgnoredTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "ease", "ease-in", "ease-out", "ease-in-out", "linear",
        "step-start", "step-end", "cubic-bezier", "steps",
        "infinite", "normal", "reverse", "alternate", "alternate-reverse",
        "none", "forwards", "backwards", "both", "running", "paused",
        "initial", "inherit", "unset", "revert", "revert-layer",
    };

    public static void EmitCss(
        string preparedLine,
        string context,
        int lineNumber,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        HashSet<string>? definitionNames,
        SymbolRecord? container)
    {
        foreach (var pattern in CssReferencePatterns)
            EmitMatches(pattern, preparedLine, context, lineNumber, references, seen, fileId, definitionNames, container);

        foreach (Match match in CssAnimationShorthandValueRegex.Matches(preparedLine))
        {
            EmitCssAnimationShorthandReferences(
                match.Groups["value"].Value,
                match.Groups["value"].Index,
                context,
                lineNumber,
                references,
                seen,
                fileId,
                definitionNames,
                container);
        }

        EmitCssClassSelectorReferences(
            preparedLine,
            context,
            lineNumber,
            references,
            seen,
            fileId,
            definitionNames,
            container);
    }

    public static void EmitScss(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (var pattern in ScssReferencePatterns)
            EmitMatches(pattern, preparedLine, context, lineNumber, references, seen, fileId, definitionNames: null, container);
    }

    private static void EmitMatches(
        ReferencePattern pattern,
        string preparedLine,
        string context,
        int lineNumber,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        HashSet<string>? definitionNames,
        SymbolRecord? container)
    {
        foreach (Match match in pattern.Regex.Matches(preparedLine))
        {
            var nameGroup = match.Groups["name"];
            if (definitionNames != null && definitionNames.Contains(nameGroup.Value))
                continue;

            if (pattern.SkipVariableDeclarations && ShouldSkipScssVariableReference(preparedLine, nameGroup.Index))
                continue;

            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                nameGroup.Value,
                nameGroup.Index,
                pattern.Kind,
                context,
                lineNumber,
                container);
        }
    }

    private static void EmitCssAnimationShorthandReferences(
        string value,
        int valueIndex,
        string context,
        int lineNumber,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        HashSet<string>? definitionNames,
        SymbolRecord? container)
    {
        var segmentStart = 0;
        var parenDepth = 0;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i < value.Length)
            {
                var ch = value[i];
                if (ch == '(')
                {
                    parenDepth++;
                    continue;
                }

                if (ch == ')' && parenDepth > 0)
                {
                    parenDepth--;
                    continue;
                }

                if (ch != ',' || parenDepth > 0)
                    continue;
            }

            EmitCssAnimationShorthandSegmentReference(
                value,
                valueIndex,
                segmentStart,
                i,
                context,
                lineNumber,
                references,
                seen,
                fileId,
                definitionNames,
                container);
            segmentStart = i + 1;
        }
    }

    private static void EmitCssAnimationShorthandSegmentReference(
        string value,
        int valueIndex,
        int segmentStart,
        int segmentEnd,
        string context,
        int lineNumber,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        HashSet<string>? definitionNames,
        SymbolRecord? container)
    {
        var cursor = segmentStart;
        while (cursor < segmentEnd && char.IsWhiteSpace(value[cursor]))
            cursor++;

        while (cursor < segmentEnd)
        {
            var tokenStart = cursor;
            while (cursor < segmentEnd && !char.IsWhiteSpace(value[cursor]))
                cursor++;

            var token = value[tokenStart..cursor];
            if (!IsCssAnimationNameToken(token))
                continue;
            if (definitionNames != null && definitionNames.Contains(token))
                return;

            ReferenceExtractor.AddReference(references, seen, fileId, token, valueIndex + tokenStart, "reference", context, lineNumber, container);
            return;
        }
    }

    private static void EmitCssClassSelectorReferences(
        string preparedLine,
        string context,
        int lineNumber,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        HashSet<string>? definitionNames,
        SymbolRecord? container)
    {
        var segmentStart = 0;
        while (segmentStart < preparedLine.Length)
        {
            var braceIndex = preparedLine.IndexOf('{', segmentStart);
            var segmentEnd = braceIndex >= 0 ? braceIndex : preparedLine.Length;
            var trimmedStart = segmentStart;
            while (trimmedStart < segmentEnd && char.IsWhiteSpace(preparedLine[trimmedStart]))
                trimmedStart++;

            if (trimmedStart < segmentEnd && preparedLine[trimmedStart] != '@')
            {
                var selectorSegment = preparedLine[trimmedStart..segmentEnd];
                foreach (var (partStart, partEnd) in EnumerateCssSelectorListSegments(selectorSegment))
                {
                    var selectorPart = selectorSegment[partStart..partEnd];
                    if (!ContainsCssClassSelectorReferenceCandidate(selectorPart))
                        continue;

                    var selectorPartTrimStart = 0;
                    while (selectorPartTrimStart < selectorPart.Length && char.IsWhiteSpace(selectorPart[selectorPartTrimStart]))
                        selectorPartTrimStart++;

                    var selectorPartBody = selectorPart[selectorPartTrimStart..];
                    foreach (Match match in CssClassSelectorReferenceRegex.Matches(selectorPartBody))
                    {
                        var nameGroup = match.Groups["name"];
                        var name = "." + nameGroup.Value;
                        if (definitionNames != null && definitionNames.Contains(name))
                            continue;

                        ReferenceExtractor.AddReference(
                            references,
                            seen,
                            fileId,
                            name,
                            trimmedStart + partStart + selectorPartTrimStart + match.Groups["name"].Index - 1,
                            "reference",
                            context,
                            lineNumber,
                            container);
                    }
                }
            }

            if (braceIndex < 0)
                break;

            segmentStart = braceIndex + 1;
        }
    }

    private static IEnumerable<(int Start, int End)> EnumerateCssSelectorListSegments(string selectorSegment)
    {
        var segmentStart = 0;
        var parenDepth = 0;
        var bracketDepth = 0;

        for (var index = 0; index < selectorSegment.Length; index++)
        {
            var ch = selectorSegment[index];
            if (ch == '(')
            {
                parenDepth++;
                continue;
            }

            if (ch == ')' && parenDepth > 0)
            {
                parenDepth--;
                continue;
            }

            if (ch == '[')
            {
                bracketDepth++;
                continue;
            }

            if (ch == ']' && bracketDepth > 0)
            {
                bracketDepth--;
                continue;
            }

            if (ch == ',' && parenDepth == 0 && bracketDepth == 0)
            {
                yield return (segmentStart, index);
                segmentStart = index + 1;
            }
        }

        yield return (segmentStart, selectorSegment.Length);
    }

    private static bool ContainsCssClassSelectorReferenceCandidate(string selectorPart)
    {
        var bracketDepth = 0;
        char quote = '\0';
        for (var index = 0; index < selectorPart.Length; index++)
        {
            var ch = selectorPart[index];
            if (quote != '\0')
            {
                if (ch == quote && (index == 0 || selectorPart[index - 1] != '\\'))
                    quote = '\0';
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                continue;
            }

            if (ch == '[')
            {
                bracketDepth++;
                continue;
            }

            if (ch == ']' && bracketDepth > 0)
            {
                bracketDepth--;
                continue;
            }

            if (bracketDepth == 0 && ch == '.')
                return true;
        }

        return false;
    }

    private static bool IsCssAnimationNameToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (CssAnimationShorthandIgnoredTokens.Contains(token))
            return false;
        if (token.IndexOf('(') >= 0 || token.IndexOf(')') >= 0 || token.IndexOf(',') >= 0
            || token.IndexOf('/') >= 0 || token.IndexOf(':') >= 0 || token.IndexOf(';') >= 0)
            return false;
        if (IsCssAnimationTimeToken(token) || IsCssAnimationNumberToken(token))
            return false;
        if (token.StartsWith("--", StringComparison.Ordinal))
            return false;
        if (!(char.IsLetter(token[0]) || token[0] == '_' || token[0] == '-'))
            return false;
        if (token[0] == '-' && token.Length > 1 && (token[1] == '-' || char.IsDigit(token[1])))
            return false;

        for (var i = 1; i < token.Length; i++)
        {
            if (char.IsLetterOrDigit(token[i]) || token[i] == '_' || token[i] == '-')
                continue;
            return false;
        }

        return true;
    }

    private static bool IsCssAnimationTimeToken(string token)
    {
        if (token.Length < 2)
            return false;

        var unitLength = token.EndsWith("ms", StringComparison.OrdinalIgnoreCase)
            ? 2
            : token.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
        if (unitLength == 0 || token.Length == unitLength)
            return false;

        var numberPart = token[..^unitLength];
        var sawDigit = false;
        var sawDot = false;
        foreach (var ch in numberPart)
        {
            if (char.IsDigit(ch))
            {
                sawDigit = true;
                continue;
            }

            if (ch == '.' && !sawDot)
            {
                sawDot = true;
                continue;
            }

            return false;
        }

        return sawDigit;
    }

    private static bool IsCssAnimationNumberToken(string token)
    {
        if (token.Length == 0 || token.IndexOfAny(['(', ')', ',', '/', ':', ';']) >= 0)
            return false;
        if (!(char.IsDigit(token[0]) || token[0] == '.'))
            return false;

        var sawDigit = false;
        var sawDot = false;
        foreach (var ch in token)
        {
            if (char.IsDigit(ch))
            {
                sawDigit = true;
                continue;
            }

            if (ch == '.' && !sawDot)
            {
                sawDot = true;
                continue;
            }

            return false;
        }

        return sawDigit;
    }

    private static bool ShouldSkipScssVariableReference(string preparedLine, int variableIndex)
    {
        var trimmed = preparedLine.TrimStart();
        if (trimmed.StartsWith("$", StringComparison.Ordinal))
        {
            var declarationColonIndex = preparedLine.IndexOf(':', variableIndex);
            if (declarationColonIndex >= 0)
                return true;
        }

        if (trimmed.StartsWith("@mixin", StringComparison.Ordinal)
            || trimmed.StartsWith("@function", StringComparison.Ordinal))
        {
            var braceIndex = preparedLine.IndexOf('{');
            if (braceIndex < 0)
                return true;
            if (variableIndex < braceIndex)
                return true;
        }

        return false;
    }
}
