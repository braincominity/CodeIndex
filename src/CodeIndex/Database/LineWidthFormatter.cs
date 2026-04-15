namespace CodeIndex.Database;

/// <summary>
/// Clamp overly wide single-line payloads so line-centered query results stay bounded.
/// 極端に長い1行のペイロードを切り詰め、行中心のクエリ結果サイズを抑える。
/// </summary>
public static class LineWidthFormatter
{
    public const int DefaultMaxLineWidth = 512;
    public const int MaxAllowedLineWidth = 4096;

    public static int ClampMaxLineWidth(int maxLineWidth) =>
        Math.Clamp(maxLineWidth, 1, MaxAllowedLineWidth);

    public static ClampedTextResult ClampLine(string line, int maxLineWidth, int? focusColumn = null, int focusLength = 1)
    {
        maxLineWidth = ClampMaxLineWidth(maxLineWidth);
        if (line.Length <= maxLineWidth)
            return new ClampedTextResult(line, false);

        if (focusColumn is > 0)
            return ClampAroundFocus(line, maxLineWidth, focusColumn.Value, Math.Max(1, focusLength));

        return ClampFromHead(line, maxLineWidth);
    }

    public static ClampedTextResult ClampLines(IReadOnlyList<string> lines, int maxLineWidth, int? focusLineIndex = null, int? focusColumn = null, int focusLength = 1)
    {
        if (lines.Count == 0)
            return new ClampedTextResult(string.Empty, false);

        maxLineWidth = ClampMaxLineWidth(maxLineWidth);
        var output = new string[lines.Count];
        var anyTruncated = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var clamped = i == focusLineIndex
                ? ClampLine(lines[i], maxLineWidth, focusColumn, focusLength)
                : ClampLine(lines[i], maxLineWidth);
            output[i] = clamped.Text;
            anyTruncated |= clamped.Truncated;
        }

        return new ClampedTextResult(string.Join('\n', output), anyTruncated);
    }

    private static ClampedTextResult ClampFromHead(string line, int maxLineWidth)
    {
        var suffix = BuildMarker(line.Length - maxLineWidth);
        var visibleWidth = maxLineWidth - suffix.Length;
        if (visibleWidth <= 0)
            return new ClampedTextResult(line[..maxLineWidth], true);

        return new ClampedTextResult(line[..visibleWidth] + suffix, true);
    }

    private static ClampedTextResult ClampAroundFocus(string line, int maxLineWidth, int focusColumn, int focusLength)
    {
        var focusStart = Math.Clamp(focusColumn - 1, 0, line.Length - 1);
        var focusEnd = Math.Min(line.Length, focusStart + focusLength);
        var start = focusStart;
        var end = focusEnd;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var prefix = start > 0 ? BuildMarker(start) : string.Empty;
            var suffix = end < line.Length ? BuildMarker(line.Length - end) : string.Empty;
            var visibleWidth = maxLineWidth - prefix.Length - suffix.Length;
            if (visibleWidth <= 0)
                return new ClampedTextResult(line.Substring(focusStart, Math.Min(maxLineWidth, line.Length - focusStart)), true);

            var desiredCenter = focusStart + ((focusEnd - focusStart) / 2);
            start = Math.Max(0, desiredCenter - (visibleWidth / 2));
            end = Math.Min(line.Length, start + visibleWidth);

            if (end < focusEnd)
            {
                end = focusEnd;
                start = Math.Max(0, end - visibleWidth);
            }

            if (start > focusStart)
            {
                start = focusStart;
                end = Math.Min(line.Length, start + visibleWidth);
            }
        }

        var finalPrefix = start > 0 ? BuildMarker(start) : string.Empty;
        var finalSuffix = end < line.Length ? BuildMarker(line.Length - end) : string.Empty;
        return new ClampedTextResult(finalPrefix + line[start..end] + finalSuffix, true);
    }

    private static string BuildMarker(int elidedChars) =>
        $"...(+{elidedChars})...";
}

public readonly record struct ClampedTextResult(string Text, bool Truncated);
