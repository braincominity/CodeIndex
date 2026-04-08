using System.Text.Json.Nodes;
using CodeIndex.Database;
using CodeIndex.Indexer;
using CodeIndex.Mcp;
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
    public void Split_EmptyFile_ReturnsNoChunks()
    {
        // Empty content produces no chunks
        // 空のコンテンツではチャンクは生成されない
        var chunks = ChunkSplitter.Split(1, "");
        Assert.Empty(chunks);
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

    [Fact]
    public void Split_CrlfInput_NormalizesToLf()
    {
        // CRLF line endings should be normalized to LF before splitting
        // CRLF改行はLFに正規化されてから分割される
        var content = "line 1\r\nline 2\r\nline 3\r\n";
        var chunks = ChunkSplitter.Split(1, content);

        Assert.Single(chunks);
        Assert.Equal(3, chunks[0].EndLine);
        Assert.DoesNotContain("\r", chunks[0].Content);
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

    [Fact]
    public void Extract_Java_DetectsClassesAndMethods()
    {
        // Java: class, interface, methods / Java: クラス、インターフェース、メソッド
        var content = "public class UserService {\n    public User getUser(int id) {\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "getUser");
    }

    [Fact]
    public void Extract_Kotlin_DetectsFunctionsAndClasses()
    {
        // Kotlin: class, fun / Kotlin: クラス、関数
        var content = "data class Config(val name: String)\nfun process(input: String): String {\n}";
        var symbols = SymbolExtractor.Extract(1, "kotlin", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "process");
    }

    [Fact]
    public void Extract_Ruby_DetectsDefAndClass()
    {
        // Ruby: def, class, module / Ruby: メソッド、クラス、モジュール
        var content = "class UserService\n  def find_user(id)\n  end\nend";
        var symbols = SymbolExtractor.Extract(1, "ruby", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "find_user");
    }

    [Fact]
    public void Extract_PHP_DetectsFunctionsAndClasses()
    {
        // PHP: function, class, interface / PHP: 関数、クラス、インターフェース
        var content = "class AuthService {\n    public function login($user) {\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "php", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "AuthService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "login");
    }

    [Fact]
    public void Extract_Swift_DetectsFuncAndStruct()
    {
        // Swift: func, class, struct / Swift: 関数、クラス、構造体
        var content = "struct Config {\n    func validate() -> Bool {\n        return true\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "swift", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "validate");
    }

    [Fact]
    public void Extract_C_DetectsFunctionsAndStructs()
    {
        // C: functions, struct / C: 関数、構造体
        var content = "typedef struct Config {\n    int value;\n};\nint main(int argc) {\n}";
        var symbols = SymbolExtractor.Extract(1, "c", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "main");
    }

    [Fact]
    public void Extract_Cpp_DetectsClassAndNamespace()
    {
        // C++: class, namespace, functions / C++: クラス、名前空間、関数
        var content = "namespace MyApp {\nclass Handler {\n    void process(int data) {\n    }\n};\n}";
        var symbols = SymbolExtractor.Extract(1, "cpp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "MyApp");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Handler");
    }

    [Fact]
    public void Extract_TypeScript_DetectsInterfaceAndEnum()
    {
        // TypeScript: interface, type, enum / TypeScript: インターフェース、型、列挙型
        var content = "export interface IUser {\n    name: string;\n}\nexport enum Status {\n    Active,\n}";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "IUser");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Status");
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
            var (record, content) = indexer.BuildRecord(filePath);

            Assert.Equal("main.py", record.Path);
            Assert.Equal("python", record.Lang);
            Assert.Equal(2, record.Lines); // "def main():\n    print('hello')\n" = 2 lines (trailing newline ignored)
            Assert.NotNull(record.Checksum);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_SkipsCaseInsensitiveDirectories()
    {
        // SkipDirs should be case-insensitive (e.g. "Build" matches "build")
        // SkipDirsは大文字小文字を区別しない（例: "Build"は"build"にマッチ）
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "app.py"), "print('hello')");

            var buildDir = Path.Combine(tempDir, "Build");
            Directory.CreateDirectory(buildDir);
            File.WriteAllText(Path.Combine(buildDir, "output.js"), "var x = 1;");

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
    public void BuildRecord_CrlfNormalizedToLf()
    {
        // CRLF line endings in files should be normalized to LF
        // ファイル内のCRLF改行はLFに正規化される
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var filePath = Path.Combine(tempDir, "crlf.py");
            File.WriteAllBytes(filePath, System.Text.Encoding.UTF8.GetBytes("line1\r\nline2\r\nline3\r\n"));

            var indexer = new FileIndexer(tempDir);
            var (record, content) = indexer.BuildRecord(filePath);

            Assert.DoesNotContain("\r", content);
            Assert.Equal(3, record.Lines);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void BuildRecord_ThrowsForOversizedFile()
    {
        // Files exceeding 10 MB should throw InvalidOperationException
        // 10MBを超えるファイルはInvalidOperationExceptionを投げる
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var filePath = Path.Combine(tempDir, "large.py");
            // Create a file just over 10 MB / 10MBを少し超えるファイルを作成
            var data = new byte[10 * 1024 * 1024 + 1];
            File.WriteAllBytes(filePath, data);

            var indexer = new FileIndexer(tempDir);
            Assert.Throws<InvalidOperationException>(() => indexer.BuildRecord(filePath));
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
    public void GetUnchangedFileId_MatchesByChecksumWhenTimestampDiffers()
    {
        var modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var checksum = "abc123def456";
        var file = new FileRecord
        {
            Path = "src/checksum.py", Lang = "python", Size = 50, Lines = 5,
            Modified = modified, Checksum = checksum,
        };
        _writer.UpsertFile(file);

        // Different timestamp but same checksum should return the ID (e.g. git checkout)
        // タイムスタンプ異なるがチェックサム一致ならIDを返す（例: git checkout）
        var newModified = modified.AddHours(1);
        var id = _writer.GetUnchangedFileId("src/checksum.py", newModified, checksum);
        Assert.NotNull(id);

        // Different timestamp AND different checksum should return null
        // タイムスタンプもチェックサムも異なるならnullを返す
        var id2 = _writer.GetUnchangedFileId("src/checksum.py", newModified.AddHours(1), "different_checksum");
        Assert.Null(id2);
    }

    [Fact]
    public void InsertChunks_InsertsAndPopulatesFts()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/test.py", Lang = "python", Size = 100, Lines = 10,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
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
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
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
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        _writer.InsertChunks([new() { FileId = fileId, ChunkIndex = 0, StartLine = 1, EndLine = 5, Content = "test" }]);
        _writer.InsertSymbols([new() { FileId = fileId, Kind = "function", Name = "test", Line = 1 }]);

        _writer.DeleteFileData(fileId);

        var (_, chunkCount, symbolCount) = _writer.GetCounts();
        Assert.Equal(0, chunkCount);
        Assert.Equal(0, symbolCount);
    }

    [Fact]
    public void CleanExistingFileData_PreventsFtsOrphans()
    {
        // Insert a file with chunks (populates FTS) / ファイルとチャンク（FTS含む）を挿入
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/orphan.py", Lang = "python", Size = 50, Lines = 5,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new() { FileId = fileId, ChunkIndex = 0, StartLine = 1, EndLine = 5, Content = "def hello_orphan_test(): pass" }]);

        // Verify FTS has the entry / FTSにエントリがあることを確認
        using var cmd1 = _db.Connection.CreateCommand();
        cmd1.CommandText = "SELECT COUNT(*) FROM fts_chunks WHERE fts_chunks MATCH 'hello_orphan_test'";
        Assert.Equal(1L, (long)cmd1.ExecuteScalar()!);

        // Clean existing data then re-upsert (simulates re-indexing)
        // 既存データを掃除してから再upsert（再インデックスをシミュレート）
        _writer.CleanExistingFileData("src/orphan.py");
        var newId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/orphan.py", Lang = "python", Size = 60, Lines = 6,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(1),
        });
        _writer.InsertChunks([new() { FileId = newId, ChunkIndex = 0, StartLine = 1, EndLine = 6, Content = "def world_replacement(): pass" }]);

        // Old FTS entry should be gone, new one should exist
        // 旧FTSエントリは消え、新エントリが存在するはず
        using var cmd2 = _db.Connection.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(*) FROM fts_chunks WHERE fts_chunks MATCH 'hello_orphan_test'";
        Assert.Equal(0L, (long)cmd2.ExecuteScalar()!);

        using var cmd3 = _db.Connection.CreateCommand();
        cmd3.CommandText = "SELECT COUNT(*) FROM fts_chunks WHERE fts_chunks MATCH 'world_replacement'";
        Assert.Equal(1L, (long)cmd3.ExecuteScalar()!);
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
                Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            _writer.UpsertFile(new FileRecord
            {
                Path = "ghost.py", Lang = "python", Size = 10, Lines = 2,
                Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
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
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        _db.DropAll();
        _db.InitializeSchema();

        var (files, chunks, symbols) = _writer.GetCounts();
        Assert.Equal(0, files);
        Assert.Equal(0, chunks);
        Assert.Equal(0, symbols);
    }

    [Fact]
    public void DeleteFileByPath_RemovesFileAndData()
    {
        // Insert a file with chunks and symbols, then delete by path
        // ファイルとチャンク・シンボルを挿入し、パスで削除
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/remove_me.py", Lang = "python", Size = 50, Lines = 5,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        _writer.InsertChunks([new() { FileId = fileId, ChunkIndex = 0, StartLine = 1, EndLine = 5, Content = "def foo(): pass" }]);
        _writer.InsertSymbols([new() { FileId = fileId, Kind = "function", Name = "foo", Line = 1 }]);

        var result = _writer.DeleteFileByPath("src/remove_me.py");
        Assert.True(result);

        var (files, chunks, symbols) = _writer.GetCounts();
        Assert.Equal(0, files);
        Assert.Equal(0, chunks);
        Assert.Equal(0, symbols);
    }

    [Fact]
    public void DeleteFileByPath_ReturnsFalseIfNotFound()
    {
        // Deleting a non-existent path returns false
        // 存在しないパスの削除はfalseを返す
        var result = _writer.DeleteFileByPath("nonexistent/file.py");
        Assert.False(result);
    }

    [Fact]
    public void DeleteFileByPath_DoesNotAffectOtherFiles()
    {
        // Deleting one file should not affect another
        // 1ファイルの削除は他のファイルに影響しない
        _writer.UpsertFile(new FileRecord
        {
            Path = "src/keep.py", Lang = "python", Size = 50, Lines = 5,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.UpsertFile(new FileRecord
        {
            Path = "src/delete.py", Lang = "python", Size = 30, Lines = 3,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        _writer.DeleteFileByPath("src/delete.py");

        var (files, _, _) = _writer.GetCounts();
        Assert.Equal(1, files);
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

/// <summary>
/// Tests for DbReader query operations.
/// DbReaderクエリ操作のテスト。
/// </summary>
public class DbReaderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContext _db;
    private readonly DbWriter _writer;
    private readonly DbReader _reader;

    public DbReaderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"codeindex_reader_test_{Guid.NewGuid():N}.db");
        _db = new DbContext(_dbPath);
        _db.InitializeSchema();
        _writer = new DbWriter(_db.Connection);
        _reader = new DbReader(_db.Connection);

        // Seed test data / テストデータを投入
        SeedData();
    }

    private void SeedData()
    {
        var pyId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/auth.py", Lang = "python", Size = 500, Lines = 30,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = pyId, ChunkIndex = 0, StartLine = 1, EndLine = 30,
            Content = "def authenticate(user, password):\n    if user == 'admin':\n        return True\n    return False",
        }]);
        _writer.InsertSymbols([
            new SymbolRecord { FileId = pyId, Kind = "function", Name = "authenticate", Line = 1 },
        ]);

        var jsId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/api.js", Lang = "javascript", Size = 800, Lines = 50,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = jsId, ChunkIndex = 0, StartLine = 1, EndLine = 50,
            Content = "export class ApiClient {\n  async fetchData(url) {\n    return fetch(url)\n  }\n}",
        }]);
        _writer.InsertSymbols([
            new SymbolRecord { FileId = jsId, Kind = "class", Name = "ApiClient", Line = 1 },
            new SymbolRecord { FileId = jsId, Kind = "function", Name = "fetchData", Line = 2 },
        ]);
    }

    [Fact]
    public void Search_FindsMatchingChunks()
    {
        var results = _reader.Search("authenticate");
        Assert.Single(results);
        Assert.Equal("src/auth.py", results[0].Path);
        Assert.Equal(1, results[0].StartLine);
    }

    [Fact]
    public void Search_ReturnsEmptyForNoMatch()
    {
        var results = _reader.Search("nonexistent_term_xyz");
        Assert.Empty(results);
    }

    [Fact]
    public void Search_FiltersByLanguage()
    {
        // "fetch" appears in JS only / "fetch"はJSのみに存在
        var jsResults = _reader.Search("fetch", lang: "javascript");
        Assert.NotEmpty(jsResults);

        var pyResults = _reader.Search("fetch", lang: "python");
        Assert.Empty(pyResults);
    }

    [Fact]
    public void Search_RespectsLimit()
    {
        var results = _reader.Search("return", limit: 1);
        Assert.Single(results);
    }

    [Fact]
    public void SearchSymbols_FindsByName()
    {
        var results = _reader.SearchSymbols("authenticate");
        Assert.Single(results);
        Assert.Equal("function", results[0].Kind);
        Assert.Equal("src/auth.py", results[0].Path);
    }

    [Fact]
    public void SearchSymbols_FiltersByKind()
    {
        var classes = _reader.SearchSymbols(kind: "class");
        Assert.Single(classes);
        Assert.Equal("ApiClient", classes[0].Name);

        var functions = _reader.SearchSymbols(kind: "function");
        Assert.Equal(2, functions.Count);
    }

    [Fact]
    public void SearchSymbols_FiltersByLanguage()
    {
        var pySymbols = _reader.SearchSymbols(lang: "python");
        Assert.Single(pySymbols);

        var jsSymbols = _reader.SearchSymbols(lang: "javascript");
        Assert.Equal(2, jsSymbols.Count);
    }

    [Fact]
    public void SearchSymbols_AllFilters()
    {
        // Combine kind + lang filter / 種別+言語フィルタの組み合わせ
        var results = _reader.SearchSymbols(query: "fetch", kind: "function", lang: "javascript");
        Assert.Single(results);
        Assert.Equal("fetchData", results[0].Name);
    }

    [Fact]
    public void ListFiles_ReturnsAllFiles()
    {
        var results = _reader.ListFiles();
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void ListFiles_FiltersByLanguage()
    {
        var results = _reader.ListFiles(lang: "python");
        Assert.Single(results);
        Assert.Equal("src/auth.py", results[0].Path);
    }

    [Fact]
    public void ListFiles_FiltersByNamePattern()
    {
        var results = _reader.ListFiles(query: "api");
        Assert.Single(results);
        Assert.Equal("src/api.js", results[0].Path);
    }

    [Fact]
    public void ListFiles_IncludesSymbolCount()
    {
        var results = _reader.ListFiles(query: "api");
        Assert.Equal(2, results[0].SymbolCount); // ApiClient + fetchData
    }

    [Fact]
    public void GetStatus_ReturnsCorrectCounts()
    {
        var status = _reader.GetStatus();
        Assert.Equal(2, status.Files);
        Assert.Equal(2, status.Chunks);
        Assert.Equal(3, status.Symbols);
    }

    [Fact]
    public void GetStatus_IncludesLanguageBreakdown()
    {
        var status = _reader.GetStatus();
        Assert.Equal(2, status.Languages.Count);
        Assert.Equal(1, status.Languages["python"]);
        Assert.Equal(1, status.Languages["javascript"]);
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}

/// <summary>
/// Tests for McpServer JSON-RPC message handling.
/// McpServerのJSON-RPCメッセージ処理のテスト。
/// </summary>
public class McpServerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContext _db;
    private readonly McpServer _server;

    public McpServerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_mcp_test_{Guid.NewGuid():N}.db");
        _db = new DbContext(_dbPath);
        _db.InitializeSchema();

        // Seed test data / テストデータを投入
        var writer = new DbWriter(_db.Connection);
        var fileId = writer.UpsertFile(new FileRecord
        {
            Path = "src/app.cs",
            Lang = "csharp",
            Size = 200,
            Lines = 10,
            Modified = new DateTime(2024, 1, 1),
            Checksum = "abc123",
        });
        writer.InsertChunks([new ChunkRecord
        {
            FileId = fileId,
            ChunkIndex = 0,
            StartLine = 1,
            EndLine = 10,
            Content = "public class App { public void Run() { } }",
        }]);
        writer.InsertSymbols([new SymbolRecord
        {
            FileId = fileId,
            Kind = "class",
            Name = "App",
            Line = 1,
        },
        new SymbolRecord
        {
            FileId = fileId,
            Kind = "function",
            Name = "Run",
            Line = 1,
        }]);

        _server = new McpServer(_dbPath, "0.1.1");
    }

    // --- Protocol tests / プロトコルテスト ---

    [Fact]
    public void Initialize_ReturnsProtocolVersion()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"test"}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal("2.0", response["jsonrpc"]!.GetValue<string>());
        Assert.Equal(1, response["id"]!.GetValue<int>());
        Assert.Equal("2024-11-05", response["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("cdidx", response["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal("0.1.1", response["result"]!["serverInfo"]!["version"]!.GetValue<string>());
    }

    [Fact]
    public void Initialize_ReturnsToolsCapability()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.NotNull(response["result"]!["capabilities"]!["tools"]);
    }

    [Fact]
    public void Notification_Initialized_ReturnsNull()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","method":"notifications/initialized"}""")!;
        var response = _server.HandleMessage(request);

        Assert.Null(response);
    }

    [Fact]
    public void Notification_Cancelled_ReturnsNull()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","method":"notifications/cancelled"}""")!;
        var response = _server.HandleMessage(request);

        Assert.Null(response);
    }

    [Fact]
    public void Ping_ReturnsEmptyResult()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":99,"method":"ping"}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(99, response["id"]!.GetValue<int>());
        Assert.NotNull(response["result"]);
    }

    [Fact]
    public void UnknownMethod_ReturnsMethodNotFound()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"unknown/method"}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(-32601, response["error"]!["code"]!.GetValue<int>());
        Assert.Contains("Method not found", response["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public void MissingMethod_ReturnsInvalidRequest()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(-32600, response["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public void MissingMethodAndId_ReturnsNull()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0"}""")!;
        var response = _server.HandleMessage(request);

        Assert.Null(response);
    }

    // --- tools/list tests / ツール一覧テスト ---

    [Fact]
    public void ToolsList_Returns5Tools()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")!;
        var response = _server.HandleMessage(request)!;

        var tools = response["result"]!["tools"]!.AsArray();
        Assert.Equal(5, tools.Count);

        var names = tools.Select(t => t!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("search", names);
        Assert.Contains("symbols", names);
        Assert.Contains("files", names);
        Assert.Contains("status", names);
        Assert.Contains("index", names);
    }

    [Fact]
    public void ToolsList_SearchHasRequiredQueryParam()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")!;
        var response = _server.HandleMessage(request)!;

        var tools = response["result"]!["tools"]!.AsArray();
        var searchTool = tools.First(t => t!["name"]!.GetValue<string>() == "search")!;
        var required = searchTool["inputSchema"]!["required"]!.AsArray();
        Assert.Contains("query", required.Select(r => r!.GetValue<string>()));
    }

    [Fact]
    public void ToolsList_IndexHasRequiredPathParam()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")!;
        var response = _server.HandleMessage(request)!;

        var tools = response["result"]!["tools"]!.AsArray();
        var indexTool = tools.First(t => t!["name"]!.GetValue<string>() == "index")!;
        var required = indexTool["inputSchema"]!["required"]!.AsArray();
        Assert.Contains("path", required.Select(r => r!.GetValue<string>()));
    }

    // --- tools/call tests / ツール呼び出しテスト ---

    [Fact]
    public void ToolsCall_Search_ReturnsResults()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search","arguments":{"query":"App"}}}""")!;
        var response = _server.HandleMessage(request)!;

        var content = response["result"]!["content"]!.AsArray();
        Assert.NotEmpty(content);
        var text = content[0]!["text"]!.GetValue<string>();
        Assert.Contains("src/app.cs", text);
    }

    [Fact]
    public void ToolsCall_Search_NoResults()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search","arguments":{"query":"nonexistent_xyz_123"}}}""")!;
        var response = _server.HandleMessage(request)!;

        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("No results found", text);
    }

    [Fact]
    public void ToolsCall_Search_MissingQuery_ReturnsError()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search","arguments":{}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.True(response["result"]!["isError"]!.GetValue<bool>());
        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("query", text);
    }

    [Fact]
    public void ToolsCall_Symbols_ReturnsResults()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"symbols","arguments":{"query":"App"}}}""")!;
        var response = _server.HandleMessage(request)!;

        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("App", text);
        Assert.Contains("class", text);
    }

    [Fact]
    public void ToolsCall_Symbols_FilterByKind()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"symbols","arguments":{"kind":"function"}}}""")!;
        var response = _server.HandleMessage(request)!;

        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Run", text);
        Assert.DoesNotContain("class", text.Split('\n').Where(l => l.Contains("Run")).First());
    }

    [Fact]
    public void ToolsCall_Files_ReturnsResults()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"files","arguments":{}}}""")!;
        var response = _server.HandleMessage(request)!;

        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("src/app.cs", text);
        Assert.Contains("csharp", text);
    }

    [Fact]
    public void ToolsCall_Status_ReturnsCounts()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"status","arguments":{}}}""")!;
        var response = _server.HandleMessage(request)!;

        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Files", text);
        Assert.Contains("Chunks", text);
        Assert.Contains("Symbols", text);
    }

    [Fact]
    public void ToolsCall_Index_MissingPath_ReturnsError()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"index","arguments":{}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.True(response["result"]!["isError"]!.GetValue<bool>());
    }

    [Fact]
    public void ToolsCall_Index_NonexistentDir_ReturnsError()
    {
        // Use a path within CWD that doesn't exist / CWD内の存在しないパスを使用
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"index","arguments":{"path":"./nonexistent_subdir_xyz_test"}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.True(response["result"]!["isError"]!.GetValue<bool>());
    }

    [Fact]
    public void ToolsCall_UnknownTool_ReturnsError()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"nonexistent","arguments":{}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(-32602, response["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public void ToolsCall_MissingToolName_ReturnsError()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"arguments":{}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(-32602, response["error"]!["code"]!.GetValue<int>());
    }

    // --- Security tests / セキュリティテスト ---

    [Fact]
    public void ToolsCall_Index_PathTraversal_ReturnsError()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"index","arguments":{"path":"/etc"}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.True(response["result"]!["isError"]!.GetValue<bool>());
        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("current working directory", text);
    }

    [Fact]
    public void ToolsCall_Search_QueryTooLong_ReturnsError()
    {
        var longQuery = new string('a', 1001);
        var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"search\",\"arguments\":{\"query\":\"" + longQuery + "\"}}}";
        var request = JsonNode.Parse(json)!;
        var response = _server.HandleMessage(request)!;

        Assert.True(response["result"]!["isError"]!.GetValue<bool>());
        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("too long", text);
    }

    // --- Database not found tests / DB未検出テスト ---

    [Fact]
    public void ToolsCall_Search_DbNotFound_ReturnsError()
    {
        var server = new McpServer("/nonexistent/path/test.db", "0.1.1");
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search","arguments":{"query":"test"}}}""")!;
        var response = server.HandleMessage(request)!;

        Assert.True(response["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("not found", response["result"]!["content"]![0]!["text"]!.GetValue<string>());
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
