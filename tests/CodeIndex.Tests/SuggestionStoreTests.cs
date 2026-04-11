using CodeIndex.Cli;
using CodeIndex.Models;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for SuggestionStore (local JSON storage with deduplication).
/// SuggestionStoreのテスト（ローカルJSON蓄積 + 重複排除）。
/// </summary>
public class SuggestionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SuggestionStore _store;

    public SuggestionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cdidx_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SuggestionStore(_tempDir);
    }

    // --- ComputeHash tests / ComputeHash テスト ---

    [Fact]
    public void ComputeHash_SameInput_ReturnsSameHash()
    {
        var hash1 = SuggestionStore.ComputeHash("symbol_extraction", "csharp", "Missing record support");
        var hash2 = SuggestionStore.ComputeHash("symbol_extraction", "csharp", "Missing record support");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentDescription_ReturnsDifferentHash()
    {
        var hash1 = SuggestionStore.ComputeHash("symbol_extraction", "csharp", "Missing record support");
        var hash2 = SuggestionStore.ComputeHash("symbol_extraction", "csharp", "Missing enum support");
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_CaseInsensitiveDescription()
    {
        // "Missing X" and "missing x" should produce the same hash
        // 「Missing X」と「missing x」は同じハッシュを返すべき
        var hash1 = SuggestionStore.ComputeHash("symbol_extraction", "csharp", "Missing Record Support");
        var hash2 = SuggestionStore.ComputeHash("symbol_extraction", "csharp", "missing record support");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_TrimsWhitespace()
    {
        var hash1 = SuggestionStore.ComputeHash("other", null, "some description");
        var hash2 = SuggestionStore.ComputeHash("other", null, "  some description  ");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_NullLanguage_TreatedAsEmpty()
    {
        var hash1 = SuggestionStore.ComputeHash("other", null, "desc");
        var hash2 = SuggestionStore.ComputeHash("other", "", "desc");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentCategory_ReturnsDifferentHash()
    {
        var hash1 = SuggestionStore.ComputeHash("symbol_extraction", "csharp", "some issue");
        var hash2 = SuggestionStore.ComputeHash("crash_report", "csharp", "some issue");
        Assert.NotEqual(hash1, hash2);
    }

    // --- TryAdd tests / TryAdd テスト ---

    [Fact]
    public void TryAdd_NewSuggestion_ReturnsTrue()
    {
        var record = MakeRecord("symbol_extraction", "csharp", "Missing record support");
        Assert.True(_store.TryAdd(record));
    }

    [Fact]
    public void TryAdd_Duplicate_ReturnsFalse()
    {
        var record1 = MakeRecord("symbol_extraction", "csharp", "Missing record support");
        var record2 = MakeRecord("symbol_extraction", "csharp", "Missing record support");

        Assert.True(_store.TryAdd(record1));
        Assert.False(_store.TryAdd(record2));
    }

    [Fact]
    public void TryAdd_DifferentSuggestions_BothSucceed()
    {
        var record1 = MakeRecord("symbol_extraction", "csharp", "Missing record support");
        var record2 = MakeRecord("language_support", "kotlin", "Add Kotlin support");

        Assert.True(_store.TryAdd(record1));
        Assert.True(_store.TryAdd(record2));

        var all = _store.LoadAll();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void TryAdd_CreatesFile()
    {
        var filePath = Path.Combine(_tempDir, "suggestions.json");
        Assert.False(File.Exists(filePath));

        _store.TryAdd(MakeRecord("other", null, "Test suggestion"));

        Assert.True(File.Exists(filePath));
    }

    // --- LoadAll tests / LoadAll テスト ---

    [Fact]
    public void LoadAll_NoFile_ReturnsEmptyList()
    {
        var all = _store.LoadAll();
        Assert.Empty(all);
    }

    [Fact]
    public void LoadAll_EmptyFile_ReturnsEmptyList()
    {
        File.WriteAllText(Path.Combine(_tempDir, "suggestions.json"), "");
        var all = _store.LoadAll();
        Assert.Empty(all);
    }

    [Fact]
    public void LoadAll_CorruptJson_ReturnsEmptyList()
    {
        File.WriteAllText(Path.Combine(_tempDir, "suggestions.json"), "{not valid json[[[");
        var all = _store.LoadAll();
        Assert.Empty(all);
    }

    [Fact]
    public void LoadAll_PreservesAllFields()
    {
        var record = new SuggestionRecord
        {
            Category = "crash_report",
            Language = "typescript",
            Description = "NullReferenceException during search",
            Context = "Searching for arrow functions",
            Hash = SuggestionStore.ComputeHash("crash_report", "typescript", "NullReferenceException during search"),
            CreatedAt = new DateTime(2026, 4, 12, 10, 0, 0, DateTimeKind.Utc),
            SubmittedToGitHub = false,
            GitHubIssueUrl = null,
        };

        _store.TryAdd(record);
        var loaded = _store.LoadAll();

        Assert.Single(loaded);
        var r = loaded[0];
        Assert.Equal("crash_report", r.Category);
        Assert.Equal("typescript", r.Language);
        Assert.Equal("NullReferenceException during search", r.Description);
        Assert.Equal("Searching for arrow functions", r.Context);
        Assert.Equal(record.Hash, r.Hash);
        Assert.False(r.SubmittedToGitHub);
        Assert.Null(r.GitHubIssueUrl);
    }

    // --- MarkSubmitted tests / MarkSubmitted テスト ---

    [Fact]
    public void MarkSubmitted_UpdatesRecord()
    {
        var record = MakeRecord("symbol_extraction", "csharp", "Missing record support");
        _store.TryAdd(record);

        _store.MarkSubmitted(record.Hash, "https://github.com/widthdom/CodeIndex/issues/99");

        var all = _store.LoadAll();
        Assert.Single(all);
        Assert.True(all[0].SubmittedToGitHub);
        Assert.Equal("https://github.com/widthdom/CodeIndex/issues/99", all[0].GitHubIssueUrl);
    }

    [Fact]
    public void MarkSubmitted_NonexistentHash_DoesNothing()
    {
        var record = MakeRecord("other", null, "Some suggestion");
        _store.TryAdd(record);

        _store.MarkSubmitted("nonexistent_hash", "https://example.com");

        var all = _store.LoadAll();
        Assert.Single(all);
        Assert.False(all[0].SubmittedToGitHub);
    }

    // --- Helpers / ヘルパー ---

    private static SuggestionRecord MakeRecord(string category, string? language, string description)
    {
        return new SuggestionRecord
        {
            Category = category,
            Language = language,
            Description = description,
            Hash = SuggestionStore.ComputeHash(category, language, description),
            CreatedAt = DateTime.UtcNow,
        };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup / ベストエフォートのクリーンアップ
        }
    }
}
