using CodeIndex.Database;
using CodeIndex.Indexer;
using CodeIndex.Models;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for ChunkSplitter.
/// ChunkSplitterのテスト。
/// </summary>
public class ChunkSplitterTests
{
    [Fact]
    public void Split_SmallFile_ReturnsSingleChunk()
    {
        // A file with fewer lines than chunk size should yield one chunk
        // チャンクサイズ未満の行数のファイルは1チャンクになる
        var content = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"line {i}"));
        var chunks = ChunkSplitter.Split(1, content);

        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(1, chunks[0].StartLine);
        Assert.Equal(10, chunks[0].EndLine);
    }

    [Fact]
    public void Split_ExactChunkSize_ReturnsSingleChunk()
    {
        // Exactly 80 lines should yield one chunk
        // ちょうど80行なら1チャンクになる
        var content = string.Join('\n', Enumerable.Range(1, 80).Select(i => $"line {i}"));
        var chunks = ChunkSplitter.Split(1, content);

        Assert.Single(chunks);
        Assert.Equal(80, chunks[0].EndLine);
    }

    [Fact]
    public void Split_LargeFile_CreatesOverlappingChunks()
    {
        // 160 lines: chunk 0 = 1-80, chunk 1 = 71-150, chunk 2 = 141-160
        // 160行: チャンク0 = 1-80, チャンク1 = 71-150, チャンク2 = 141-160
        var content = string.Join('\n', Enumerable.Range(1, 160).Select(i => $"line {i}"));
        var chunks = ChunkSplitter.Split(1, content);

        Assert.True(chunks.Count >= 3);

        // Second chunk should start at line 71 (overlap of 10)
        // 2番目のチャンクは71行目から始まる（10行の重複）
        Assert.Equal(71, chunks[1].StartLine);
    }

    [Fact]
    public void Split_EmptyFile_ReturnsSingleEmptyChunk()
    {
        // Empty content should still produce one chunk
        // 空のコンテンツでも1チャンクが生成される
        var chunks = ChunkSplitter.Split(1, "");
        Assert.Single(chunks);
    }

    [Fact]
    public void Split_ChunkIndex_IsSequential()
    {
        // Chunk indices should be sequential starting from 0
        // チャンクインデックスは0から連番になる
        var content = string.Join('\n', Enumerable.Range(1, 200).Select(i => $"line {i}"));
        var chunks = ChunkSplitter.Split(1, content);

        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
        }
    }
}

/// <summary>
/// Tests for SymbolExtractor.
/// SymbolExtractorのテスト。
/// </summary>
public class SymbolExtractorTests
{
    [Fact]
    public void Extract_Python_DetectsFunctions()
    {
        // Should detect both sync and async functions
        // 同期・非同期関数を検出する
        var content = "def authenticate(user):\n    pass\nasync def fetch_data():\n    pass";
        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.Equal(2, symbols.Count);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "authenticate");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "fetch_data");
    }

    [Fact]
    public void Extract_Python_DetectsClasses()
    {
        var content = "class UserService:\n    pass";
        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.Single(symbols);
        Assert.Equal("class", symbols[0].Kind);
        Assert.Equal("UserService", symbols[0].Name);
    }

    [Fact]
    public void Extract_JavaScript_DetectsFunctionsAndClasses()
    {
        // Should detect exported and non-exported functions and classes
        // export有無にかかわらず関数とクラスを検出する
        var content = "export function login() {}\nclass AuthService {}\nimport React from 'react'";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "login");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "AuthService");
        Assert.Contains(symbols, s => s.Kind == "import");
    }

    [Fact]
    public void Extract_CSharp_DetectsClassesAndMethods()
    {
        var content = "public class UserService\n{\n    public async Task<User> GetUser(int id)\n    {\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetUser");
    }

    [Fact]
    public void Extract_Go_DetectsFunctions()
    {
        // Should detect both regular and method functions
        // 通常関数とメソッド関数の両方を検出する
        var content = "func NewHandler() *Handler {\n}\nfunc (h *Handler) ServeHTTP(w http.ResponseWriter, r *http.Request) {\n}";
        var symbols = SymbolExtractor.Extract(1, "go", content);

        Assert.Equal(2, symbols.Count);
        Assert.Contains(symbols, s => s.Name == "NewHandler");
        Assert.Contains(symbols, s => s.Name == "ServeHTTP");
    }

    [Fact]
    public void Extract_Rust_DetectsFunctionsAndStructs()
    {
        var content = "pub fn handle_request() {}\npub struct Config {}\nimpl Config {";
        var symbols = SymbolExtractor.Extract(1, "rust", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "handle_request");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
    }

    [Fact]
    public void Extract_UnknownLang_ReturnsEmpty()
    {
        // Unsupported languages return no symbols
        // 未サポート言語は空を返す
        var symbols = SymbolExtractor.Extract(1, "markdown", "# Heading");
        Assert.Empty(symbols);
    }

    [Fact]
    public void Extract_NullLang_ReturnsEmpty()
    {
        var symbols = SymbolExtractor.Extract(1, null, "some content");
        Assert.Empty(symbols);
    }

    [Fact]
    public void Extract_LineNumbers_AreOneBased()
    {
        // Line numbers should be 1-based
        // 行番号は1始まりであること
        var content = "x = 1\ndef foo():\n    pass";
        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.Single(symbols);
        Assert.Equal(2, symbols[0].Line);
    }
}

/// <summary>
/// Tests for FileIndexer.
/// FileIndexerのテスト。
/// </summary>
public class FileIndexerTests
{
    [Theory]
    [InlineData("test.py", "python")]
    [InlineData("app.js", "javascript")]
    [InlineData("main.ts", "typescript")]
    [InlineData("lib.go", "go")]
    [InlineData("mod.rs", "rust")]
    [InlineData("App.java", "java")]
    [InlineData("Service.cs", "csharp")]
    [InlineData("style.css", "css")]
    [InlineData("style.scss", "css")]
    [InlineData("page.vue", "vue")]
    [InlineData("page.svelte", "svelte")]
    [InlineData("main.tf", "terraform")]
    public void DetectLanguage_KnownExtensions_ReturnsCorrectLang(string filename, string expected)
    {
        Assert.Equal(expected, FileIndexer.DetectLanguage(filename));
    }

    [Theory]
    [InlineData("image.png")]
    [InlineData("data.bin")]
    [InlineData("archive.zip")]
    public void DetectLanguage_UnknownExtensions_ReturnsNull(string filename)
    {
        Assert.Null(FileIndexer.DetectLanguage(filename));
    }

    [Fact]
    public void ScanFiles_SkipsExcludedDirectories()
    {
        // Create a temp directory structure to test scanning
        // テスト用の一時ディレクトリ構造を作成
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "app.py"), "print('hello')");

            var nodeModules = Path.Combine(tempDir, "node_modules");
            Directory.CreateDirectory(nodeModules);
            File.WriteAllText(Path.Combine(nodeModules, "dep.js"), "module.exports = {}");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles();

            Assert.Single(files);
            Assert.Contains("app.py", files[0]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_SkipsExcludedFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "app.js"), "console.log('hello')");
            File.WriteAllText(Path.Combine(tempDir, "package-lock.json"), "{}");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles();

            // Only app.js should be found, not package-lock.json
            // app.jsのみ検出され、package-lock.jsonは除外される
            Assert.Single(files);
            Assert.Contains("app.js", files[0]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void BuildRecord_CreatesCorrectRecord()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var filePath = Path.Combine(tempDir, "main.py");
            File.WriteAllText(filePath, "def main():\n    print('hello')\n");

            var indexer = new FileIndexer(tempDir);
            var record = indexer.BuildRecord(filePath);

            Assert.Equal("main.py", record.Path);
            Assert.Equal("python", record.Lang);
            Assert.Equal(3, record.Lines);
            Assert.NotNull(record.Checksum);
            Assert.Contains("def main()", record.Snippet);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}

/// <summary>
/// Tests for DbContext and DbWriter integration.
/// DbContextとDbWriterの統合テスト。
/// </summary>
public class DatabaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContext _db;
    private readonly DbWriter _writer;

    public DatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}.db");
        _db = new DbContext(_dbPath);
        _db.InitializeSchema();
        _writer = new DbWriter(_db.Connection);
    }

    [Fact]
    public void InitializeSchema_CreatesAllTables()
    {
        // Verify tables exist by querying sqlite_master
        // sqlite_masterを問い合わせてテーブルの存在を確認
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        var tables = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            tables.Add(reader.GetString(0));

        Assert.Contains("files", tables);
        Assert.Contains("chunks", tables);
        Assert.Contains("symbols", tables);
        Assert.Contains("fts_chunks", tables);
    }

    [Fact]
    public void UpsertFile_InsertsAndReturnsId()
    {
        var file = new FileRecord
        {
            Path = "src/main.py",
            Lang = "python",
            Size = 100,
            Lines = 10,
            Snippet = "def main():",
            Checksum = "abc123",
            Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var id = _writer.UpsertFile(file);
        Assert.True(id > 0);
    }

    [Fact]
    public void UpsertFile_ReplacesOnConflict()
    {
        // Same path should replace (not duplicate)
        // 同一パスは置換される（重複しない）
        var file1 = new FileRecord
        {
            Path = "src/app.py", Lang = "python", Size = 100, Lines = 10,
            Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        var file2 = new FileRecord
        {
            Path = "src/app.py", Lang = "python", Size = 200, Lines = 20,
            Modified = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        _writer.UpsertFile(file1);
        _writer.UpsertFile(file2);

        var (count, _, _) = _writer.GetCounts();
        Assert.Equal(1, count);
    }

    [Fact]
    public void GetUnchangedFileId_ReturnIdIfUnchanged()
    {
        var modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var file = new FileRecord
        {
            Path = "src/lib.py", Lang = "python", Size = 50, Lines = 5,
            Modified = modified,
        };
        _writer.UpsertFile(file);

        // Same modified time should return the ID
        // 同一更新日時ならIDを返す
        var id = _writer.GetUnchangedFileId("src/lib.py", modified);
        Assert.NotNull(id);

        // Different modified time should return null
        // 異なる更新日時ならnullを返す
        var id2 = _writer.GetUnchangedFileId("src/lib.py", modified.AddHours(1));
        Assert.Null(id2);
    }

    [Fact]
    public void InsertChunks_InsertsAndPopulatesFts()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/test.py", Lang = "python", Size = 100, Lines = 10,
            Modified = DateTime.UtcNow,
        });

        var chunks = new List<ChunkRecord>
        {
            new() { FileId = fileId, ChunkIndex = 0, StartLine = 1, EndLine = 10, Content = "def authenticate(user):" },
        };
        _writer.InsertChunks(chunks);

        // Verify FTS search works / FTS検索が動作することを確認
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT content FROM fts_chunks WHERE fts_chunks MATCH 'authenticate'";
        var result = cmd.ExecuteScalar() as string;
        Assert.NotNull(result);
        Assert.Contains("authenticate", result);
    }

    [Fact]
    public void InsertSymbols_InsertsCorrectly()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/svc.py", Lang = "python", Size = 50, Lines = 5,
            Modified = DateTime.UtcNow,
        });

        var symbols = new List<SymbolRecord>
        {
            new() { FileId = fileId, Kind = "function", Name = "process", Line = 1 },
            new() { FileId = fileId, Kind = "class", Name = "Service", Line = 5 },
        };
        _writer.InsertSymbols(symbols);

        var (_, _, symbolCount) = _writer.GetCounts();
        Assert.Equal(2, symbolCount);
    }

    [Fact]
    public void DeleteFileData_RemovesChunksAndSymbols()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/del.py", Lang = "python", Size = 50, Lines = 5,
            Modified = DateTime.UtcNow,
        });

        _writer.InsertChunks([new() { FileId = fileId, ChunkIndex = 0, StartLine = 1, EndLine = 5, Content = "test" }]);
        _writer.InsertSymbols([new() { FileId = fileId, Kind = "function", Name = "test", Line = 1 }]);

        _writer.DeleteFileData(fileId);

        var (_, chunkCount, symbolCount) = _writer.GetCounts();
        Assert.Equal(0, chunkCount);
        Assert.Equal(0, symbolCount);
    }

    [Fact]
    public void PurgeStaleFiles_RemovesDeletedFiles()
    {
        // Simulate branch switch: insert a file, then purge when file doesn't exist
        // ブランチ切り替えをシミュレート: ファイルを挿入後、存在しないファイルをパージ
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_purge_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create a real file and a "ghost" file entry
            // 実在するファイルと「ゴースト」ファイルエントリを作成
            var realFile = Path.Combine(tempDir, "real.py");
            File.WriteAllText(realFile, "x = 1");

            _writer.UpsertFile(new FileRecord
            {
                Path = "real.py", Lang = "python", Size = 5, Lines = 1,
                Modified = DateTime.UtcNow,
            });
            _writer.UpsertFile(new FileRecord
            {
                Path = "ghost.py", Lang = "python", Size = 10, Lines = 2,
                Modified = DateTime.UtcNow,
            });

            var (beforeCount, _, _) = _writer.GetCounts();
            Assert.Equal(2, beforeCount);

            var purged = _writer.PurgeStaleFiles(tempDir);
            Assert.Equal(1, purged);

            var (afterCount, _, _) = _writer.GetCounts();
            Assert.Equal(1, afterCount);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DropAll_RemovesAllTables()
    {
        // Insert some data, then drop all
        // データを挿入してから全削除
        _writer.UpsertFile(new FileRecord
        {
            Path = "src/x.py", Lang = "python", Size = 10, Lines = 1,
            Modified = DateTime.UtcNow,
        });

        _db.DropAll();
        _db.InitializeSchema();

        var (files, chunks, symbols) = _writer.GetCounts();
        Assert.Equal(0, files);
        Assert.Equal(0, chunks);
        Assert.Equal(0, symbols);
    }

    public void Dispose()
    {
        _db.Dispose();

        // Clear SQLite connection pool to release file locks on Windows
        // Windows環境でファイルロックを解放するためコネクションプールをクリア
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
