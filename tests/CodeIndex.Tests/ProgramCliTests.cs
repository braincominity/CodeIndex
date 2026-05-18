using CodeIndex.Cli;
using CodeIndex.Models;
using System.Text.Json;

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

    [Fact]
    public void Suggestions_ListFiltersAndPrintsStoredSuggestions()
    {
        using var fixture = SuggestionFixture.Create();
        var csharp = fixture.Add("symbol_extraction", "csharp", "Missing record extraction", submitted: false);
        fixture.Add("language_support", "rust", "Improve macro handling", submitted: true);

        var (exitCode, stdout, stderr) = RunCliInSubprocess(["suggestions", "list", "--db", fixture.DbPath, "--category", "symbol_extraction"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr);
        Assert.Contains(csharp.Hash[..12], stdout);
        Assert.Contains("draft", stdout);
        Assert.Contains("Missing record extraction", stdout);
        Assert.DoesNotContain("Improve macro handling", stdout);
    }

    [Fact]
    public void Suggestions_ShowJsonResolvesShortId()
    {
        using var fixture = SuggestionFixture.Create();
        var record = fixture.Add("output_format", "python", "JSON export needed", submitted: true);

        var (exitCode, stdout, stderr) = RunCliInSubprocess(["suggestions", "show", record.Hash[..12], "--db", fixture.DbPath, "--json"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal(record.Hash, doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("submitted_pending_triage", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("JSON export needed", doc.RootElement.GetProperty("description").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("submit_attempt_count").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("last_submit_attempt", out _));
        Assert.False(doc.RootElement.TryGetProperty("last_submit_error", out _));
    }

    [Fact]
    public void Suggestions_ListJsonIncludesSubmitDiagnostics()
    {
        using var fixture = SuggestionFixture.Create();
        var attemptedAt = new DateTime(2026, 5, 17, 4, 3, 2, DateTimeKind.Utc);
        fixture.Add(
            "output_format",
            "python",
            "JSON export failed",
            submitted: false,
            lastSubmitAttempt: attemptedAt,
            submitAttemptCount: 2,
            lastSubmitError: "API 422: validation failed");

        var (exitCode, stdout, stderr) = RunCliInSubprocess(["suggestions", "list", "--db", fixture.DbPath, "--json"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal(2, doc.RootElement.GetProperty("submit_attempt_count").GetInt32());
        Assert.Equal(attemptedAt, doc.RootElement.GetProperty("last_submit_attempt").GetDateTime());
        Assert.Equal("API 422: validation failed", doc.RootElement.GetProperty("last_submit_error").GetString());
    }

    [Fact]
    public void Suggestions_ExportMarkdownIncludesFilteredSuggestions()
    {
        using var fixture = SuggestionFixture.Create();
        var attemptedAt = new DateTime(2026, 5, 17, 4, 3, 2, DateTimeKind.Utc);
        fixture.Add(
            "output_format",
            "csharp",
            "Share triage notes",
            submitted: false,
            lastSubmitAttempt: attemptedAt,
            submitAttemptCount: 1,
            lastSubmitError: "HttpRequestException: network unavailable");
        fixture.Add("language_support", "ruby", "Add parser support", submitted: false);

        var (exitCode, stdout, stderr) = RunCliInSubprocess(["suggestions", "export", "--db", fixture.DbPath, "--language", "csharp", "--format", "markdown"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr);
        Assert.Contains("# cdidx Suggestions", stdout);
        Assert.Contains("Share triage notes", stdout);
        Assert.Contains("- last_submit_attempt: `2026-05-17T04:03:02.0000000Z`", stdout);
        Assert.Contains("- submit_attempt_count: `1`", stdout);
        Assert.Contains("- last_submit_error: `HttpRequestException: network unavailable`", stdout);
        Assert.DoesNotContain("Add parser support", stdout);
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

    private sealed class SuggestionFixture : IDisposable
    {
        private readonly string _root;
        private readonly List<SuggestionRecord> _records = new();

        private SuggestionFixture(string root)
        {
            _root = root;
            DbPath = Path.Combine(root, ".cdidx", "codeindex.db");
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        }

        public string DbPath { get; }

        public static SuggestionFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"cdidx_suggestions_cli_{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new SuggestionFixture(root);
        }

        public SuggestionRecord Add(
            string category,
            string? language,
            string description,
            bool submitted,
            DateTime? lastSubmitAttempt = null,
            int submitAttemptCount = 0,
            string? lastSubmitError = null)
        {
            var record = new SuggestionRecord
            {
                Category = category,
                Language = language,
                Description = description,
                Context = "Agent noticed this during repository triage.",
                Hash = SuggestionStore.ComputeHash(category, language, description),
                CreatedAt = new DateTime(2026, 5, 16, 12, _records.Count, 0, DateTimeKind.Utc),
                SubmittedToGitHub = submitted,
                GitHubIssueUrl = submitted ? "https://github.com/Widthdom/CodeIndex/issues/99" : null,
                LastSubmitAttempt = lastSubmitAttempt,
                SubmitAttemptCount = submitAttemptCount,
                LastSubmitError = lastSubmitError,
            };
            _records.Add(record);
            Write();
            return record;
        }

        private void Write()
        {
            var path = Path.Combine(_root, ".cdidx", "suggestions-codeindex.json");
            var json = JsonSerializer.Serialize(_records, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true,
            });
            File.WriteAllText(path, json);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
