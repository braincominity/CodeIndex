namespace CodeIndex.Tests;

/// <summary>
/// Black-box CLI tests for Program entrypoint behavior.
/// Program エントリポイント挙動のブラックボックステスト。
/// </summary>
public class ProgramCliTests
{
    [Theory]
    [InlineData("mcp", "--db", "Error: --db requires a value.")]
    [InlineData("mcp", "--db", "--json", "Error: --db requires a value.")]
    [InlineData("mcp", "--since", "nope", "Error: could not parse --since value 'nope' as a date/time.")]
    public void Mcp_InvalidArgumentsReturnUsageError(string command, string arg1, string arg2OrExpected, string? expectedError = null)
    {
        var args = expectedError == null
            ? new[] { command, arg1 }
            : new[] { command, arg1, arg2OrExpected };
        var expected = expectedError ?? arg2OrExpected;

        var (exitCode, _, stderr) = RunCliInSubprocess(args);

        Assert.Equal(1, exitCode);
        Assert.Contains(expected, stderr);
        Assert.Contains("Usage: cdidx mcp [--db <path>]", stderr);
        Assert.DoesNotContain("MCP server running", stderr);
    }

    [Fact]
    public void Mcp_UnsupportedOptionReturnUsageError()
    {
        var (exitCode, _, stderr) = RunCliInSubprocess(["mcp", "--json"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Error: --json is not supported for mcp.", stderr);
        Assert.Contains("Usage: cdidx mcp [--db <path>]", stderr);
        Assert.DoesNotContain("Warning: unknown option", stderr);
    }

    [Fact]
    public void Mcp_DbAcceptsLeadingDoubleDashPathValueViaInlineLiteral()
    {
        var (exitCode, _, stderr) = RunCliInSubprocess(["mcp", "--db=--tmp.db"]);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("requires a value", stderr);
    }

    [Fact]
    public void Mcp_DbAcceptsRecognizedOptionTokenViaInlineValue()
    {
        var (exitCode, _, stderr) = RunCliInSubprocess(["mcp", "--db=--json"]);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("requires a value", stderr);
    }

    [Fact]
    public void Mcp_DbRejectsSeparatedUnknownDoubleDashValue()
    {
        var (exitCode, _, stderr) = RunCliInSubprocess(["mcp", "--db", "--mystery"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Error: --db requires a value.", stderr);
        Assert.Contains("`--db=<value>`", stderr);
        Assert.Contains("Usage: cdidx mcp [--db <path>]", stderr);
    }

    [Fact]
    public void Mcp_DbRejectsEmptyInlineValue()
    {
        var (exitCode, _, stderr) = RunCliInSubprocess(["mcp", "--db="]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Error: --db requires a value.", stderr);
        Assert.Contains("Usage: cdidx mcp [--db <path>]", stderr);
    }

    [Fact]
    public void Symbols_NameHelpLikeValueReturnsUsageError()
    {
        var (exitCode, stdout, stderr) = RunCliInSubprocess(["symbols", "--name", "-h"]);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("--name requires a value", stderr);
        Assert.DoesNotContain("██████╗", stderr);
    }

    [Fact]
    public void Completions_HelpLikeValueReturnsCompletionsError()
    {
        var (exitCode, stdout, stderr) = RunCliInSubprocess(["--completions", "-h"]);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("requires a shell value, got option-like token '-h'", stderr);
        Assert.Contains("Usage: cdidx --completions <shell>", stderr);
    }

    [Fact]
    public void Completions_MissingShellReturnsUsageError()
    {
        var (exitCode, stdout, stderr) = RunCliInSubprocess(["--completions"]);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("--completions requires a shell value", stderr);
        Assert.Contains("Usage: cdidx --completions <shell>", stderr);
        Assert.DoesNotContain("Unknown command: --completions", stderr);
    }

    [Fact]
    public void Completions_OptionLikeShellTokenReturnsUsageError()
    {
        var (exitCode, stdout, stderr) = RunCliInSubprocess(["--completions", "--json"]);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("requires a shell value, got option-like token '--json'", stderr);
        Assert.Contains("Usage: cdidx --completions <shell>", stderr);
        Assert.DoesNotContain("Unknown shell", stderr);
    }

    [Fact]
    public void Completions_ExtraArgsReturnUsageError()
    {
        var (exitCode, stdout, stderr) = RunCliInSubprocess(["--completions", "bash", "extra"]);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("accepts exactly one shell value", stderr);
        Assert.Contains("Usage: cdidx --completions <shell>", stderr);
    }

    [Theory]
    [InlineData("license")]
    [InlineData("--license")]
    public void License_PrintsLicenseSummary(string arg)
    {
        var (exitCode, stdout, stderr) = RunCliInSubprocess([arg]);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr);
        Assert.Contains("Functional Source License, Version 1.1, ALv2 Future License (FSL-1.1-ALv2)", stdout);
        Assert.Contains("non-competing purposes", stdout);
        Assert.Contains("Competing commercial products or services require a separate written agreement", stdout);
        Assert.Contains("separate written agreement", stdout);
        Assert.Contains("LICENSES/Apache-2.0.txt", stdout);
        Assert.Contains("INTEGRATION_POLICY.md", stdout);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunCliInSubprocess(string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = GetRepositoryRoot(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(GetBuiltCliDllPath());
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start cdidx subprocess / cdidx サブプロセスの起動に失敗");
        process.StandardInput.Close();
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdOut, stdErr);
    }

    private static string GetBuiltCliDllPath()
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory).Name;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
        var fallbackConfigurations = new[] { configuration, "Debug", "Release" }
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            foreach (var candidateConfiguration in fallbackConfigurations)
            {
                var candidate = Path.Combine(dir.FullName, "src", "CodeIndex", "bin", candidateConfiguration!, tfm, "cdidx.dll");
                if (File.Exists(candidate))
                    return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate built cdidx.dll from test output path / テスト出力パスから cdidx.dll を特定できませんでした");
    }

    private static string GetRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CodeIndex.sln")) || Directory.Exists(Path.Combine(dir.FullName, "src", "CodeIndex")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root / リポジトリルートを特定できませんでした");
    }
}
