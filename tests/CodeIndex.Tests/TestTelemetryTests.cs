using CodeIndex.TestTelemetry;

namespace CodeIndex.Tests;

public sealed class TestTelemetryTests
{
    [Fact]
    public void Load_SummarizesTrxResultsAndSlowestTests()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_trx_telemetry");
        try
        {
            var resultsDirectory = Path.Combine(projectRoot, "TestResults");
            Directory.CreateDirectory(resultsDirectory);

            File.WriteAllText(Path.Combine(resultsDirectory, "test_results.trx"), """
                <?xml version="1.0" encoding="utf-8"?>
                <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                  <Results>
                    <UnitTestResult testName="FastPass" outcome="Passed" duration="00:00:00.1000000" />
                    <UnitTestResult testName="SlowPass" outcome="Passed" duration="00:00:03.5000000" />
                    <UnitTestResult testName="BrokenTest" outcome="Failed" duration="00:00:01.2500000" />
                    <UnitTestResult testName="SkippedTest" outcome="NotExecuted" />
                  </Results>
                </TestRun>
                """);

            var summary = TrxTelemetry.Load(resultsDirectory, top: 2);

            Assert.Equal(1, summary.TrxFileCount);
            Assert.Equal(4, summary.Total);
            Assert.Equal(2, summary.Passed);
            Assert.Equal(1, summary.Failed);
            Assert.Equal(1, summary.Skipped);
            Assert.Equal(["SlowPass", "BrokenTest"], summary.Slowest.Select(result => result.TestName));
            Assert.Equal(["BrokenTest"], summary.Failures.Select(result => result.TestName));
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Render_IncludesFailureAndRuntimeTelemetry()
    {
        var summary = new TrxTelemetrySummary(
            ResultsDirectory: "TestResults",
            TrxFileCount: 1,
            Total: 2,
            Passed: 1,
            Failed: 1,
            Skipped: 0,
            Other: 0,
            Slowest: [new TrxTestResult("SlowTest", "Passed", TimeSpan.FromSeconds(2.25))],
            Failures: [new TrxTestResult("FailedTest", "Failed", TimeSpan.FromMilliseconds(500))],
            Warnings: []);

        var output = TrxTelemetryRenderer.Render(summary);

        Assert.Contains("TRX telemetry summary", output, StringComparison.Ordinal);
        Assert.Contains("Tests: 2; passed: 1; failed: 1; skipped: 0; other: 0", output, StringComparison.Ordinal);
        Assert.Contains("- FailedTest (Failed, 500ms)", output, StringComparison.Ordinal);
        Assert.Contains("- SlowTest (Passed, 2.250s)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MissingDirectoryReturnsWarningInsteadOfFailingCiSummary()
    {
        var summary = TrxTelemetry.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), top: 10);

        Assert.Equal(0, summary.Total);
        Assert.Single(summary.Warnings);
        Assert.Contains("Results directory not found", summary.Warnings[0], StringComparison.Ordinal);
    }
}
