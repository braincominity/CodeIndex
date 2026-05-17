using System.Globalization;
using System.Xml.Linq;

namespace CodeIndex.TestTelemetry;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            if (args[0] != "summarize")
                throw new TelemetryException($"Unknown command '{args[0]}'.");

            var options = ParseSummarizeOptions(args[1..]);
            var summary = TrxTelemetry.Load(options.ResultsDirectory, options.Top);
            Console.Out.Write(TrxTelemetryRenderer.Render(summary));
            return 0;
        }
        catch (TelemetryException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static bool IsHelp(string arg) => arg is "-h" or "--help" or "help";

    private static void PrintUsage()
    {
        Console.Out.WriteLine("Usage:");
        Console.Out.WriteLine("  dotnet run --project tools/CodeIndex.TestTelemetry -- summarize --results-directory ./TestResults [--top 10]");
    }

    private static SummarizeOptions ParseSummarizeOptions(string[] args)
    {
        var resultsDirectory = "TestResults";
        var top = 10;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--results-directory")
            {
                if (i + 1 >= args.Length)
                    throw new TelemetryException("Missing value for --results-directory.");

                resultsDirectory = args[++i];
                continue;
            }

            if (arg == "--top")
            {
                if (i + 1 >= args.Length)
                    throw new TelemetryException("Missing value for --top.");

                if (!int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out top) || top <= 0)
                    throw new TelemetryException("--top must be a positive integer.");

                continue;
            }

            throw new TelemetryException($"Unknown option '{arg}'.");
        }

        return new SummarizeOptions(resultsDirectory, top);
    }

    private sealed record SummarizeOptions(string ResultsDirectory, int Top);
}

public static class TrxTelemetry
{
    public static TrxTelemetrySummary Load(string resultsDirectory, int top)
    {
        if (top <= 0)
            throw new TelemetryException("Top count must be positive.");

        if (!Directory.Exists(resultsDirectory))
        {
            return new TrxTelemetrySummary(
                ResultsDirectory: resultsDirectory,
                TrxFileCount: 0,
                Total: 0,
                Passed: 0,
                Failed: 0,
                Skipped: 0,
                Other: 0,
                Slowest: [],
                Failures: [],
                Warnings: [$"Results directory not found: {resultsDirectory}"]);
        }

        var trxFiles = Directory
            .EnumerateFiles(resultsDirectory, "*.trx", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var tests = new List<TrxTestResult>();
        var warnings = new List<string>();

        foreach (var path in trxFiles)
        {
            try
            {
                tests.AddRange(ReadResults(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                warnings.Add($"Could not parse {path}: {ex.Message}");
            }
        }

        var passed = tests.Count(result => IsOutcome(result, "Passed"));
        var failed = tests.Count(result => IsOutcome(result, "Failed") || IsOutcome(result, "Error"));
        var skipped = tests.Count(result => IsOutcome(result, "NotExecuted") || IsOutcome(result, "Skipped"));
        var other = tests.Count - passed - failed - skipped;

        var slowest = tests
            .OrderByDescending(result => result.Duration)
            .ThenBy(result => result.TestName, StringComparer.Ordinal)
            .Take(top)
            .ToList();

        var failures = tests
            .Where(result => IsOutcome(result, "Failed") || IsOutcome(result, "Error"))
            .OrderByDescending(result => result.Duration)
            .ThenBy(result => result.TestName, StringComparer.Ordinal)
            .Take(top)
            .ToList();

        return new TrxTelemetrySummary(
            ResultsDirectory: resultsDirectory,
            TrxFileCount: trxFiles.Count,
            Total: tests.Count,
            Passed: passed,
            Failed: failed,
            Skipped: skipped,
            Other: other,
            Slowest: slowest,
            Failures: failures,
            Warnings: warnings);
    }

    private static IEnumerable<TrxTestResult> ReadResults(string path)
    {
        var document = XDocument.Load(path, LoadOptions.None);
        foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "UnitTestResult"))
        {
            var testName = (string?)element.Attribute("testName");
            var outcome = (string?)element.Attribute("outcome");

            if (string.IsNullOrWhiteSpace(testName) || string.IsNullOrWhiteSpace(outcome))
                continue;

            yield return new TrxTestResult(
                TestName: testName.Trim(),
                Outcome: outcome.Trim(),
                Duration: ParseDuration((string?)element.Attribute("duration")));
        }
    }

    private static TimeSpan ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return TimeSpan.Zero;

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var duration)
            ? duration
            : TimeSpan.Zero;
    }

    private static bool IsOutcome(TrxTestResult result, string outcome) =>
        string.Equals(result.Outcome, outcome, StringComparison.OrdinalIgnoreCase);
}

public static class TrxTelemetryRenderer
{
    public static string Render(TrxTelemetrySummary summary)
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        writer.WriteLine("TRX telemetry summary");
        writer.WriteLine($"Results directory: {summary.ResultsDirectory}");
        writer.WriteLine($"TRX files: {summary.TrxFileCount}");
        writer.WriteLine($"Tests: {summary.Total}; passed: {summary.Passed}; failed: {summary.Failed}; skipped: {summary.Skipped}; other: {summary.Other}");

        foreach (var warning in summary.Warnings)
        {
            writer.WriteLine($"Warning: {warning}");
        }

        if (summary.Failures.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Failed tests:");
            foreach (var result in summary.Failures)
            {
                writer.WriteLine($"- {result.TestName} ({result.Outcome}, {FormatDuration(result.Duration)})");
            }
        }

        if (summary.Slowest.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Slowest tests:");
            foreach (var result in summary.Slowest)
            {
                writer.WriteLine($"- {result.TestName} ({result.Outcome}, {FormatDuration(result.Duration)})");
            }
        }

        return writer.ToString();
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalSeconds >= 1
            ? $"{duration.TotalSeconds:F3}s"
            : $"{duration.TotalMilliseconds:F0}ms";
}

public sealed record TrxTelemetrySummary(
    string ResultsDirectory,
    int TrxFileCount,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    int Other,
    IReadOnlyList<TrxTestResult> Slowest,
    IReadOnlyList<TrxTestResult> Failures,
    IReadOnlyList<string> Warnings);

public sealed record TrxTestResult(string TestName, string Outcome, TimeSpan Duration);

public sealed class TelemetryException(string message) : Exception(message);
