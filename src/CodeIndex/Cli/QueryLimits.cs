namespace CodeIndex.Cli;

internal static class QueryLimits
{
    internal const int MaxQueryLength = 1000;

    internal static string FormatQueryTooLongError()
        => $"Query too long (max {MaxQueryLength} characters)";
}
