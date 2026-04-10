using CodeIndex.Database;
using CodeIndex.Models;

namespace CodeIndex.Tests;

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
            new SymbolRecord
            {
                FileId = pyId, Kind = "function", Name = "authenticate", Line = 1,
                StartLine = 1, EndLine = 4, BodyStartLine = 2, BodyEndLine = 4,
                Signature = "def authenticate(user, password):"
            },
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
            new SymbolRecord
            {
                FileId = jsId, Kind = "class", Name = "ApiClient", Line = 1,
                StartLine = 1, EndLine = 4, BodyStartLine = 1, BodyEndLine = 4,
                Signature = "export class ApiClient {", Visibility = "export"
            },
            new SymbolRecord
            {
                FileId = jsId, Kind = "function", Name = "fetchData", Line = 2,
                StartLine = 2, EndLine = 3, BodyStartLine = 2, BodyEndLine = 3,
                Signature = "async fetchData(url) {", ContainerKind = "class", ContainerName = "ApiClient"
            },
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
    public void Search_PrefersSourceFilesOverTests()
    {
        var testFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "tests/auth_test.py", Lang = "python", Size = 300, Lines = 10,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = testFileId, ChunkIndex = 0, StartLine = 1, EndLine = 3,
            Content = "def authenticate_test_case():\n    authenticate('a', 'b')\n    return True",
        }]);

        var results = _reader.Search("authenticate", limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("src/auth.py", results[0].Path);
        Assert.Equal("tests/auth_test.py", results[1].Path);
    }

    [Fact]
    public void Search_PrefersDefinitionFileOverReferenceOnlySourceFile()
    {
        var refFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/session.py", Lang = "python", Size = 300, Lines = 10,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = refFileId, ChunkIndex = 0, StartLine = 1, EndLine = 3,
            Content = "def login(user, password):\n    return authenticate(user, password)\n",
        }]);

        var results = _reader.Search("authenticate", limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("src/auth.py", results[0].Path);
        Assert.Equal("src/session.py", results[1].Path);
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
    public void Search_RawQuerySupportsFtsPrefixSyntax()
    {
        var results = _reader.Search("auth*", rawQuery: true);

        Assert.Single(results);
        Assert.Equal("src/auth.py", results[0].Path);
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
    public void SearchSymbols_ReturnsRichMetadataWhenAvailable()
    {
        var results = _reader.SearchSymbols("fetchData");

        var symbol = Assert.Single(results);
        Assert.Equal(2, symbol.StartLine);
        Assert.Equal(3, symbol.EndLine);
        Assert.Equal(2, symbol.BodyStartLine);
        Assert.Equal(3, symbol.BodyEndLine);
        Assert.Equal("ApiClient", symbol.ContainerName);
        Assert.Equal("class", symbol.ContainerKind);
        Assert.Equal("async fetchData(url) {", symbol.Signature);
    }

    [Fact]
    public void GetExcerpt_ReconstructsRequestedLineRange()
    {
        var excerpt = _reader.GetExcerpt("src/auth.py", 1, 2);

        Assert.NotNull(excerpt);
        Assert.Equal(1, excerpt!.StartLine);
        Assert.Equal(2, excerpt.EndLine);
        Assert.Contains("def authenticate(user, password):", excerpt.Content);
        Assert.Contains("if user == 'admin':", excerpt.Content);
    }

    [Fact]
    public void GetDefinitions_ReturnsDefinitionContentAndOptionalBody()
    {
        var results = _reader.GetDefinitions("authenticate", includeBody: true);

        var definition = Assert.Single(results);
        Assert.Contains("def authenticate(user, password):", definition.Content);
        Assert.NotNull(definition.BodyContent);
        Assert.Contains("return True", definition.BodyContent);
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
    public void SearchSymbols_ExcludeTests_RemovesLikelyTestPaths()
    {
        var testFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "tests/auth_test.py", Lang = "python", Size = 300, Lines = 10,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertSymbols([
            new SymbolRecord { FileId = testFileId, Kind = "function", Name = "authenticate", Line = 1, StartLine = 1, EndLine = 1 },
        ]);

        var results = _reader.SearchSymbols(query: "authenticate", excludeTests: true);

        Assert.Single(results);
        Assert.Equal("src/auth.py", results[0].Path);
    }

    [Fact]
    public void Search_ExcludeTests_RemovesLikelyTestPaths()
    {
        var testFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "tests/auth_test.py", Lang = "python", Size = 300, Lines = 10,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = testFileId, ChunkIndex = 0, StartLine = 1, EndLine = 3,
            Content = "def authenticate_test_case():\n    authenticate('a', 'b')\n    return True",
        }]);

        var results = _reader.Search("authenticate", limit: 5, excludeTests: true);

        Assert.Single(results);
        Assert.Equal("src/auth.py", results[0].Path);
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
    public void ListFiles_PathFiltersAndExcludePaths_WorkTogether()
    {
        var results = _reader.ListFiles(pathPattern: "src/", excludePathPatterns: ["api"]);

        Assert.Single(results);
        Assert.Equal("src/auth.py", results[0].Path);
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
