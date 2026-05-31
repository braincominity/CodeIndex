using System.Diagnostics;

namespace CodeIndex;

public static class CodeIndexTelemetry
{
    public const string ActivitySourceName = "CodeIndex";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
