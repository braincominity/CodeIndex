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
        var filePath = Path.Combine(_tempDir, "suggestions-codeindex.json");
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
        File.WriteAllText(Path.Combine(_tempDir, "suggestions-codeindex.json"), "");
        var all = _store.LoadAll();
        Assert.Empty(all);
    }

    [Fact]
    public void LoadAll_CorruptJson_ReturnsEmptyList()
    {
        File.WriteAllText(Path.Combine(_tempDir, "suggestions-codeindex.json"), "{not valid json[[[");
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

    [Fact]
    public void LoadByStatus_ReturnsOnlyMatchingSubmissionState()
    {
        var submitted = MakeRecord("other", null, "Submitted suggestion");
        submitted.SubmittedToGitHub = true;
        submitted.GitHubIssueUrl = "https://github.com/widthdom/CodeIndex/issues/1";
        var unsubmitted = MakeRecord("other", null, "Unsubmitted suggestion");

        _store.TryAdd(submitted);
        _store.TryAdd(unsubmitted);

        var loaded = _store.LoadByStatus(submittedToGitHub: false);

        Assert.Single(loaded);
        Assert.Equal(unsubmitted.Hash, loaded[0].Hash);
    }

    [Fact]
    public void LoadSince_ReturnsSuggestionsAtOrAfterThreshold()
    {
        var older = MakeRecord("other", null, "Older suggestion");
        older.CreatedAt = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
        var boundary = MakeRecord("other", null, "Boundary suggestion");
        boundary.CreatedAt = new DateTime(2026, 5, 2, 9, 0, 0, DateTimeKind.Utc);
        var newer = MakeRecord("other", null, "Newer suggestion");
        newer.CreatedAt = new DateTime(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc);

        _store.TryAdd(older);
        _store.TryAdd(boundary);
        _store.TryAdd(newer);

        var loaded = _store.LoadSince(new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal(new[] { boundary.Hash, newer.Hash }, loaded.Select(s => s.Hash));
    }

    [Fact]
    public void LoadByCategory_IsCaseInsensitive()
    {
        var crash = MakeRecord("crash_report", "csharp", "Crash suggestion");
        var other = MakeRecord("other", "csharp", "Other suggestion");

        _store.TryAdd(crash);
        _store.TryAdd(other);

        var loaded = _store.LoadByCategory("CRASH_REPORT");

        Assert.Single(loaded);
        Assert.Equal(crash.Hash, loaded[0].Hash);
    }

    [Fact]
    public void LoadByLanguage_IsCaseInsensitive()
    {
        var csharp = MakeRecord("other", "csharp", "CSharp suggestion");
        var python = MakeRecord("other", "python", "Python suggestion");

        _store.TryAdd(csharp);
        _store.TryAdd(python);

        var loaded = _store.LoadByLanguage("CSHARP");

        Assert.Single(loaded);
        Assert.Equal(csharp.Hash, loaded[0].Hash);
    }

    [Fact]
    public void Load_ReturnsRequestedPageInStoredOrder()
    {
        var first = MakeRecord("other", null, "First suggestion");
        var second = MakeRecord("other", null, "Second suggestion");
        var third = MakeRecord("other", null, "Third suggestion");
        var fourth = MakeRecord("other", null, "Fourth suggestion");

        _store.TryAdd(first);
        _store.TryAdd(second);
        _store.TryAdd(third);
        _store.TryAdd(fourth);

        var loaded = _store.Load(skip: 1, take: 2);

        Assert.Equal(new[] { second.Hash, third.Hash }, loaded.Select(s => s.Hash));
    }

    [Fact]
    public void Load_FilteredCorruptJson_ReturnsEmptyListAndPreservesBackup()
    {
        var filePath = Path.Combine(_tempDir, "suggestions-codeindex.json");
        var backupPath = filePath + ".bak";
        File.WriteAllText(filePath, "{not valid json[[[");

        var loaded = _store.LoadByCategory("other");

        Assert.Empty(loaded);
        Assert.True(File.Exists(backupPath), "Corrupt file should be preserved as .bak");
        Assert.False(File.Exists(filePath), "Original corrupt file should be removed");
    }

    [Fact]
    public void Load_FilteredWhitespaceOnlyFile_ReturnsEmptyListWithoutBackup()
    {
        var filePath = Path.Combine(_tempDir, "suggestions-codeindex.json");
        var backupPath = filePath + ".bak";
        File.WriteAllText(filePath, " \r\n\t ");

        var loaded = _store.LoadByLanguage("csharp");

        Assert.Empty(loaded);
        Assert.True(File.Exists(filePath), "Whitespace-only file should remain the live store.");
        Assert.False(File.Exists(backupPath), "Whitespace-only file should not be treated as corrupt.");
    }

    [Fact]
    public void StreamingReadShare_AllowsConcurrentReplacementWrite()
    {
        var filePath = Path.Combine(_tempDir, "suggestions-codeindex.json");
        _store.TryAdd(MakeRecord("other", null, "Existing suggestion"));

        using var readHandle = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            SuggestionStore.StreamingReadFileShare);

        var record = MakeRecord("other", null, "Concurrent write suggestion");

        var ex = Record.Exception(() => _store.TryAdd(record));

        Assert.Null(ex);
        Assert.Contains(_store.LoadAll(), s => s.Hash == record.Hash);
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

    // --- Atomic write and corruption recovery tests / アトミック書き込みと破損復旧テスト ---

    [Fact]
    public void CorruptFile_IsPreservedAsBackup()
    {
        // Write a corrupt file, then load — should rename to .bak
        // 破損ファイルを書き込み、ロード — .bak にリネームされるべき
        var filePath = Path.Combine(_tempDir, "suggestions-codeindex.json");
        var backupPath = filePath + ".bak";
        File.WriteAllText(filePath, "{corrupt json[[[");

        var all = _store.LoadAll();
        Assert.Empty(all);
        Assert.True(File.Exists(backupPath), "Corrupt file should be preserved as .bak");
        Assert.False(File.Exists(filePath), "Original corrupt file should be removed");
    }

    [Fact]
    public void AtomicWrite_SurvivesAddAfterCorruption()
    {
        // After corruption recovery, new suggestions should work normally
        // 破損復旧後、新しい提案が正常に動作するべき
        File.WriteAllText(Path.Combine(_tempDir, "suggestions-codeindex.json"), "not json");

        _store.LoadAll(); // triggers backup
        var record = MakeRecord("other", null, "Post-corruption suggestion");
        Assert.True(_store.TryAdd(record));

        var all = _store.LoadAll();
        Assert.Single(all);
        Assert.Equal("Post-corruption suggestion", all[0].Description);
    }

    [Fact]
    public void TryAdd_MoveFailure_DoesNotLeaveOrphanTmpFile()
    {
        // Force File.Move to fail by pre-creating the destination as a directory.
        // The write-to-temp succeeds, but the rename onto a directory throws and
        // the temp file must be cleaned up so `.cdidx/` does not accumulate orphans (#1574).
        // File.Move を失敗させるため、宛先パスをディレクトリとして事前作成する。
        // 一時ファイルへの書き込みは成功するが、ディレクトリに対する rename は失敗するため、
        // `.cdidx/` に孤児が蓄積しないよう一時ファイルがクリーンアップされる必要がある (#1574)。
        var filePath = Path.Combine(_tempDir, "suggestions-codeindex.json");
        var tmpPath = filePath + ".tmp";
        Directory.CreateDirectory(filePath);

        var record = MakeRecord("other", null, "Move failure cleanup");
        var ex = Record.Exception(() => _store.TryAdd(record));

        Assert.NotNull(ex);
        Assert.False(File.Exists(tmpPath), $"Orphan .tmp file should be cleaned up after Move failure: {tmpPath}");
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
