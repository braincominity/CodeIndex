using CodeIndex.Indexer;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CodeIndex.Tests;

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
    [InlineData("app.dart", "dart")]
    [InlineData("Main.scala", "scala")]
    [InlineData("analysis.r", "r")]
    [InlineData("analysis.R", "r")]
    [InlineData("web.ex", "elixir")]
    [InlineData("test.exs", "elixir")]
    [InlineData("script.lua", "lua")]
    [InlineData("Program.fs", "fsharp")]
    [InlineData("Script.fsx", "fsharp")]
    [InlineData("Module1.vb", "vb")]
    [InlineData("script.vbs", "vb")]
    [InlineData("Index.cshtml", "csharp")]
    [InlineData("Counter.razor", "csharp")]
    [InlineData("MainWindow.xaml", "xml")]
    [InlineData("App.axaml", "xml")]
    [InlineData("MyApp.csproj", "xml")]
    [InlineData("Main.hs", "haskell")]
    [InlineData("main.zig", "zig")]
    [InlineData("schema.proto", "protobuf")]
    [InlineData("schema.graphql", "graphql")]
    [InlineData("build.gradle", "gradle")]
    [InlineData("build.cmake", "cmake")]
    [InlineData("script.ps1", "powershell")]
    [InlineData("run.bat", "batch")]
    [InlineData("run.cmd", "batch")]
    [InlineData("script.bash", "shell")]
    [InlineData("script.zsh", "shell")]
    [InlineData("script.fish", "shell")]
    [InlineData("Dockerfile", "dockerfile")]
    [InlineData("Makefile", "makefile")]
    [InlineData("Justfile", "justfile")]
    [InlineData("CMakeLists.txt", "cmake")]
    [InlineData("Vagrantfile", "ruby")]
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

    [Theory]
    [InlineData("rbenv", "#!/usr/bin/env bash\nexit 0\n", "shell")]
    [InlineData("tool", "#!/bin/sh\necho hi\n", "shell")]
    [InlineData("worker", "#!/usr/bin/python3\nprint('hi')\n", "python")]
    [InlineData("bundle", "#!/usr/bin/env ruby\nputs 'hi'\n", "ruby")]
    [InlineData("cli", "#!/usr/bin/env node\nconsole.log('hi')\n", "javascript")]
    [InlineData("script", "#!/usr/bin/env pwsh\nWrite-Host hi\n", "powershell")]
    public void DetectLanguage_ExtensionlessShebangScripts_ReturnCorrectLang(string fileName, string content, string expected)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var path = Path.Combine(tempDir, fileName);
            File.WriteAllText(path, content);

            Assert.Equal(expected, FileIndexer.DetectLanguage(path));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DetectLanguage_ExtensionlessNonScript_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var path = Path.Combine(tempDir, "README");
            File.WriteAllText(path, "Hello world\n");

            Assert.Null(FileIndexer.DetectLanguage(path));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DetectLanguage_UnknownExtensionWithShebang_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var path = Path.Combine(tempDir, "notes.txt");
            File.WriteAllText(path, "#!/usr/bin/env bash\necho hi\n");

            Assert.Null(FileIndexer.DetectLanguage(path));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DetectLanguage_LeadingWhitespacePseudoShebang_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var path = Path.Combine(tempDir, "tool");
            File.WriteAllText(path, "  #!/usr/bin/env bash\necho hi\n");

            Assert.Null(FileIndexer.DetectLanguage(path));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
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

    [Theory]
    [InlineData("node_modules", "dep.js")]
    [InlineData("target", "main.rs")]
    [InlineData("vendor", "dep.go")]
    [InlineData("bin", "app.cs")]
    public void ScanFiles_IndexesExplicitRootEvenWhenRootNameIsSkipped(string rootDirName, string fileName)
    {
        var tempParentDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        var rootDir = Path.Combine(tempParentDir, rootDirName);
        try
        {
            Directory.CreateDirectory(rootDir);
            File.WriteAllText(Path.Combine(rootDir, fileName), "content");

            var nestedNodeModules = Path.Combine(rootDir, "node_modules");
            Directory.CreateDirectory(nestedNodeModules);
            File.WriteAllText(Path.Combine(nestedNodeModules, "nested.js"), "module.exports = {}");

            var indexer = new FileIndexer(rootDir);
            var files = indexer.ScanFiles();

            Assert.Single(files);
            Assert.Contains(fileName, files[0]);
        }
        finally
        {
            if (Directory.Exists(tempParentDir))
                Directory.Delete(tempParentDir, true);
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
            File.WriteAllText(Path.Combine(tempDir, "Cargo.lock"), "# lock");
            File.WriteAllText(Path.Combine(tempDir, "Gemfile.lock"), "GEM");

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
    public void ScanFiles_IncludesExtensionlessShebangScripts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "rbenv-init"), "#!/usr/bin/env bash\necho init\n");
            File.WriteAllText(Path.Combine(tempDir, "python-tool"), "#!/usr/bin/python3\nprint('hi')\n");
            File.WriteAllText(Path.Combine(tempDir, "plain-text"), "Hello world\n");
            File.WriteAllText(Path.Combine(tempDir, "known.rb"), "puts 'known'\n");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(["known.rb", "python-tool", "rbenv-init"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_ExcludesUnknownExtensionEvenWhenShebangLooksSupported()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "notes.txt"), "#!/usr/bin/env bash\necho hi\n");
            File.WriteAllText(Path.Combine(tempDir, "script"), "#!/usr/bin/env bash\necho hi\n");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(["script"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ScanFiles_IgnoresUnixFifoWithoutHanging()
    {
        if (OperatingSystem.IsWindows())
            return;

        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            CreateUnixFifo(Path.Combine(tempDir, "tool"));
            CreateUnixFifo(Path.Combine(tempDir, "tool.sh"));
            CreateUnixFifo(Path.Combine(tempDir, "Dockerfile"));

            var indexer = new FileIndexer(tempDir);
            var scanTask = Task.Run(() => indexer.ScanFiles());
            var completedTask = await Task.WhenAny(scanTask, Task.Delay(TimeSpan.FromSeconds(2)));

            Assert.Same(scanTask, completedTask);
            Assert.Empty(await scanTask);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void BuildRecord_HandlesUnicodeAndCjkContent()
    {
        // Files with Unicode/CJK characters in content should be indexed correctly
        // Unicode/CJK文字を含むファイルが正しくインデックスされること
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var content = "// コメント: 日本語テスト\npublic class 日本語クラス\n{\n    public string 名前 { get; set; }\n    // 中文注释\n    // 한국어 주석\n}\n";
            var filePath = Path.Combine(tempDir, "unicode.cs");
            File.WriteAllText(filePath, content);

            var indexer = new FileIndexer(tempDir);
            var (record, fileContent, warning) = indexer.BuildRecord(filePath);

            Assert.Equal("unicode.cs", record.Path);
            Assert.Equal("csharp", record.Lang);
            Assert.Null(warning); // Valid UTF-8, no warning / 有効なUTF-8なので警告なし
            Assert.Contains("日本語クラス", fileContent);
            Assert.Contains("中文注释", fileContent);
            Assert.Contains("한국어", fileContent);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void BuildRecord_CjkSymbolsExtractedCorrectly()
    {
        var content = "// 日本語コメント\npublic class ユーザーサービス\n{\n    public string 名前を取得(int id) { return \"\"; }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        // CJK class and method names should be extracted / CJKのクラス名・メソッド名が抽出されること
        // Note: \w in .NET regex matches Unicode letters, so CJK identifiers work
        // 注: .NET の \w は Unicode 文字にマッチするため CJK 識別子も動作する
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "ユーザーサービス");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "名前を取得");
    }

    [Fact]
    public void BuildRecord_NormalizesPathSeparators()
    {
        // Ensure Windows-style backslashes are converted to forward slashes
        // Windows形式のバックスラッシュがフォワードスラッシュに変換されることを確認
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            var subDir = Path.Combine(tempDir, "src", "models");
            Directory.CreateDirectory(subDir);
            var filePath = Path.Combine(subDir, "user.py");
            File.WriteAllText(filePath, "class User: pass\n");

            var indexer = new FileIndexer(tempDir);
            var (record, _, _) = indexer.BuildRecord(filePath);

            // Path should use forward slashes regardless of OS
            // OSに関わらずフォワードスラッシュを使うべき
            Assert.DoesNotContain("\\", record.Path);
            Assert.Contains("/", record.Path);
            Assert.Equal("src/models/user.py", record.Path);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_IncludesFileNameBasedLanguages()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "Dockerfile"), "FROM alpine");
            File.WriteAllText(Path.Combine(tempDir, "Makefile"), "all: build");
            File.WriteAllText(Path.Combine(tempDir, "app.py"), "print('hello')");
            File.WriteAllText(Path.Combine(tempDir, "unknown.xyz"), "nothing");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles();

            // Dockerfile, Makefile, and app.py should be found; unknown.xyz should not
            Assert.Equal(3, files.Count);
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
            var (record, content, _) = indexer.BuildRecord(filePath);

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
            var (record, content, _) = indexer.BuildRecord(filePath);

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

    [Fact]
    public void BuildRecord_ExtensionlessShebangScriptUsesDetectedLanguage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var filePath = Path.Combine(tempDir, "rbenv-hooks");
            File.WriteAllText(filePath, "#!/usr/bin/env bash\necho hooks\n");

            var indexer = new FileIndexer(tempDir);
            var (record, _, warning) = indexer.BuildRecord(filePath);

            Assert.Equal("shell", record.Lang);
            Assert.Null(warning);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static void CreateUnixFifo(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "mkfifo",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(path);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start mkfifo / mkfifo の起動に失敗");
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"mkfifo failed: {stderr.Trim()}");
    }
}
