using CodeIndex.Changelog;

namespace CodeIndex.Tests;

public sealed class ChangelogToolTests
{
    [Fact]
    public void PrepareMovesFragmentsIntoReleaseAndUpdatesFooter()
    {
        using var scope = new TestRepositoryScope();
        scope.WriteFile("CHANGELOG.md", SampleChangelog);
        scope.WriteFile("version.json", """
            {
              "version": "1.16.0"
            }
            """);
        scope.WriteFile("changelog.d/unreleased/195.fixed.md", SampleFragment);
        scope.WriteFile("changelog.d/unreleased/.gitkeep", string.Empty);

        var tool = new ChangelogTool(scope.Root);
        var result = tool.Prepare(new Version(1, 17, 0), new DateOnly(2026, 5, 1), writeChanges: true);

        Assert.Contains("Prepared changelog for v1.17.0.", result.Summary);
        Assert.Contains("Previous version: v1.16.0.", result.Summary);
        Assert.Contains("Fragments consumed: 1.", result.Summary);

        var changelog = scope.ReadFile("CHANGELOG.md");
        Assert.Equal(2, CountOccurrences(changelog, "### [1.17.0] - 2026-05-01"));
        Assert.Contains("English release note", changelog);
        Assert.Contains("Japanese release note", changelog);
        Assert.Contains("Existing English unreleased note", changelog);
        Assert.Contains("Existing Japanese unreleased note", changelog);
        Assert.Contains("### [Unreleased]\n\n### [1.17.0] - 2026-05-01", changelog.Replace("\r\n", "\n"));
        Assert.Contains("[Unreleased]: https://github.com/Widthdom/CodeIndex/compare/v1.17.0...HEAD", changelog);
        Assert.Contains("[1.17.0]: https://github.com/Widthdom/CodeIndex/compare/v1.16.0...v1.17.0", changelog);
        Assert.Contains("[1.0.0]: https://github.com/Widthdom/CodeIndex/releases/tag/v1.0.0", changelog);
        Assert.Equal("""
            {
              "version": "1.17.0"
            }
            """.Replace("\r\n", "\n") + "\n", scope.ReadFile("version.json").Replace("\r\n", "\n"));
        Assert.False(scope.Exists("changelog.d/unreleased/195.fixed.md"));
    }

    [Fact]
    public void PrepareRerunPreservesExistingReleaseAndAppendsNewFragments()
    {
        using var scope = new TestRepositoryScope();
        scope.WriteFile("CHANGELOG.md", SampleChangelog);
        scope.WriteFile("version.json", """
            {
              "version": "1.16.0"
            }
            """);
        scope.WriteFile("changelog.d/unreleased/195.fixed.md", SampleFragment);
        scope.WriteFile("changelog.d/unreleased/.gitkeep", string.Empty);

        var tool = new ChangelogTool(scope.Root);
        tool.Prepare(new Version(1, 17, 0), new DateOnly(2026, 5, 1), writeChanges: true);

        scope.WriteFile("changelog.d/unreleased/+release-process.docs.md", """
            ---
            category: docs
            affected:
              - .codex/workflows/release-changelog.md
            ---

            ## English

            - **Release changelog workflow documented** — release preparation now has a dedicated workflow.

            ## 日本語

            - **Release changelog ワークフローを文書化** — release preparation 用の専用ワークフローを追加しました。
            """);

        scope.WriteFile("version.json", """
            {
              "version": "1.17.0"
            }
            """);

        tool.Prepare(new Version(1, 17, 0), new DateOnly(2026, 5, 1), writeChanges: true);

        var changelog = scope.ReadFile("CHANGELOG.md");
        Assert.Equal(2, CountOccurrences(changelog, "### [1.17.0] - 2026-05-01"));
        Assert.Contains("English release note", changelog);
        Assert.Contains("Japanese release note", changelog);
        Assert.Contains("release preparation now has a dedicated workflow", changelog);
        Assert.Contains("release preparation 用の専用ワークフロー", changelog);
        Assert.Contains("[Unreleased]: https://github.com/Widthdom/CodeIndex/compare/v1.17.0...HEAD", changelog);
        Assert.Equal(0, scope.ListFiles("changelog.d/unreleased").Count(path => Path.GetFileName(path) is "195.fixed.md" or "+release-process.docs.md"));
    }

    [Fact]
    public void CheckFragmentsRejectsCategoryMismatch()
    {
        using var scope = new TestRepositoryScope();
        scope.WriteFile("CHANGELOG.md", SampleChangelog);
        scope.WriteFile("version.json", """
            {
              "version": "1.16.0"
            }
            """);
        scope.WriteFile("changelog.d/unreleased/+bad.fixed.md", """
            ---
            category: changed
            issues:
              - 195
            ---

            ## English

            - **Bad fragment** — invalid category.

            ## 日本語

            - **Bad fragment** — invalid category.
            """);

        var tool = new ChangelogTool(scope.Root);
        var ex = Assert.Throws<ChangelogException>(() => tool.CheckFragments());
        Assert.Contains("file name category 'fixed' does not match front matter category 'changed'", ex.Message);
    }

    [Fact]
    public void CheckFragmentsRejectsMissingJapaneseSection()
    {
        using var scope = new TestRepositoryScope();
        scope.WriteFile("CHANGELOG.md", SampleChangelog);
        scope.WriteFile("version.json", """
            {
              "version": "1.16.0"
            }
            """);
        scope.WriteFile("changelog.d/unreleased/+missing-jp.fixed.md", """
            ---
            category: fixed
            ---

            ## English

            - **Bad fragment** — missing Japanese section.
            """);

        var tool = new ChangelogTool(scope.Root);
        var ex = Assert.Throws<ChangelogException>(() => tool.CheckFragments());
        Assert.Contains("missing '## 日本語' heading", ex.Message);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class TestRepositoryScope : IDisposable
    {
        private readonly string _previousDirectory;
        public string Root { get; }

        public TestRepositoryScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "codeindex-changelog-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            _previousDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(Root);
        }

        public void WriteFile(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
        }

        public string ReadFile(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

        public bool Exists(string relativePath) => File.Exists(Path.Combine(Root, relativePath));

        public IReadOnlyList<string> ListFiles(string relativePath) => Directory.Exists(Path.Combine(Root, relativePath))
            ? Directory.GetFiles(Path.Combine(Root, relativePath), "*", SearchOption.AllDirectories)
            : [];

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_previousDirectory);
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private const string SampleChangelog = """
        # Changelog

        All notable changes to this project will be documented in this file.

        ## English

        ### [Unreleased]

        #### Fixed
        - Existing English unreleased note.

        ## 日本語

        ### [Unreleased]

        #### 修正
        - Existing Japanese unreleased note.

        [Unreleased]: https://github.com/Widthdom/CodeIndex/compare/v1.16.0...HEAD
        [1.16.0]: https://github.com/Widthdom/CodeIndex/compare/v1.15.3...v1.16.0
        [1.0.0]: https://github.com/Widthdom/CodeIndex/releases/tag/v1.0.0
        """;

    private const string SampleFragment = """
        ---
        category: fixed
        issues:
          - 195
        affected:
          - src/CodeIndex/Cli/QueryCommandRunner.cs
        ---

        ## English

        - **English release note (#195)** — fragment content.

        ## 日本語

        - **Japanese release note (#195)** — fragment content.
        """;
}
