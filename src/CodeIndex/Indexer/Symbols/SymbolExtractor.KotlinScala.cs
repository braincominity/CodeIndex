using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

public static partial class SymbolExtractor
{
    private static bool TryFindKotlinScalaExpressionBodyEndLine(string line, int startColumn)
    {
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var inBlockComment = false;
        var inString = false;
        var inChar = false;

        for (var i = Math.Max(0, startColumn); i < line.Length; i++)
        {
            var c = line[i];

            if (inBlockComment)
            {
                if (c == '*' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (inString)
            {
                if (c == '\\' && i + 1 < line.Length)
                {
                    i++;
                    continue;
                }

                if (c == '"')
                    inString = false;
                continue;
            }

            if (inChar)
            {
                if (c == '\\' && i + 1 < line.Length)
                {
                    i++;
                    continue;
                }

                if (c == '\'')
                    inChar = false;
                continue;
            }

            if (c == '/' && i + 1 < line.Length)
            {
                if (line[i + 1] == '/')
                    break;

                if (line[i + 1] == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '\'')
            {
                inChar = true;
                continue;
            }

            if (c == '(')
            {
                parenDepth++;
                continue;
            }

            if (c == ')' && parenDepth > 0)
            {
                parenDepth--;
                continue;
            }

            if (c == '[')
            {
                bracketDepth++;
                continue;
            }

            if (c == ']' && bracketDepth > 0)
            {
                bracketDepth--;
                continue;
            }

            if (c == '{')
            {
                braceDepth++;
                continue;
            }

            if (c == '}' && braceDepth > 0)
            {
                braceDepth--;
                continue;
            }

            if (c != '=' || parenDepth > 0 || bracketDepth > 0 || braceDepth > 0)
                continue;

            if (i + 1 < line.Length && (line[i + 1] == '=' || line[i + 1] == '>'))
                continue;

            var next = i + 1;
            while (next < line.Length)
            {
                if (char.IsWhiteSpace(line[next]))
                {
                    next++;
                    continue;
                }

                if (line[next] == '/' && next + 1 < line.Length)
                {
                    if (line[next + 1] == '/')
                        return false;

                    if (line[next + 1] == '*')
                    {
                        var commentEnd = line.IndexOf("*/", next + 2, StringComparison.Ordinal);
                        if (commentEnd < 0)
                            return false;

                        next = commentEnd + 2;
                        continue;
                    }
                }

                return line[next] != '{';
            }

            return false;
        }

        return false;
    }

}
