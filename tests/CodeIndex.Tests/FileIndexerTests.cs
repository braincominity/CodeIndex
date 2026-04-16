using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using CodeIndex.Cli;
using CodeIndex.Indexer;

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
    [InlineData("app.cjs", "javascript")]
    [InlineData("app.mjs", "javascript")]
    [InlineData("main.ts", "typescript")]
    [InlineData("main.cts", "typescript")]
    [InlineData("main.mts", "typescript")]
    [InlineData("types.d.cts", "typescript")]
    [InlineData("types.d.mts", "typescript")]
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
    public void ScanFiles_SkipsExplicitRootWhenRootNameIsSkipped(string rootDirName, string fileName)
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

            Assert.Empty(files);
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
    public void ScanFiles_RespectsGitignorePatternsAndNegation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "secret.py\nbuild_output/\n*.generated.js\n!keep.generated.js\n");
            File.WriteAllText(Path.Combine(tempDir, "keep.py"), "print('keep')");
            File.WriteAllText(Path.Combine(tempDir, "secret.py"), "print('secret')");
            File.WriteAllText(Path.Combine(tempDir, "app.generated.js"), "export const ignored = true;");
            File.WriteAllText(Path.Combine(tempDir, "keep.generated.js"), "export const kept = true;");
            Directory.CreateDirectory(Path.Combine(tempDir, "build_output"));
            File.WriteAllText(Path.Combine(tempDir, "build_output", "inside.py"), "print('ignored')");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "keep.generated.js", "keep.py"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_RespectsCdidxignoreAndNestedGitignore()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "src"));
            Directory.CreateDirectory(Path.Combine(tempDir, "fixtures"));
            File.WriteAllText(Path.Combine(tempDir, ".cdidxignore"), "fixtures/\n*.cache.js\n");
            File.WriteAllText(Path.Combine(tempDir, "src", ".gitignore"), "*.generated.cs\n");
            File.WriteAllText(Path.Combine(tempDir, "src", "Service.cs"), "public class Service { }");
            File.WriteAllText(Path.Combine(tempDir, "src", "Generated.generated.cs"), "public class Generated { }");
            File.WriteAllText(Path.Combine(tempDir, "fixtures", "sample.py"), "print('fixture')");
            File.WriteAllText(Path.Combine(tempDir, "app.cache.js"), "export const cache = true;");
            File.WriteAllText(Path.Combine(tempDir, "app.js"), "export const app = true;");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(["app.js", "src/.gitignore", "src/Service.cs"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_FailsClosedWhenRootIgnoreFileIsUnreadable()
    {
        if (OperatingSystem.IsWindows())
            return;

        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        var ignorePath = Path.Combine(tempDir, ".gitignore");
        UnixFileMode? originalMode = null;
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(ignorePath, "secret.py\n");
            File.WriteAllText(Path.Combine(tempDir, "secret.py"), "print('secret')");
            File.WriteAllText(Path.Combine(tempDir, "keep.py"), "print('keep')");
            originalMode = File.GetUnixFileMode(ignorePath);
            SetUnixPermissions(ignorePath, UnixFileMode.None);

            var indexer = new FileIndexer(tempDir);
            var result = indexer.ScanFilesDetailed();

            Assert.Empty(result.Files);
            Assert.Contains(result.Errors, error => error.Path == ".gitignore" && error.Message == "Could not read .gitignore.");
        }
        finally
        {
            if (originalMode.HasValue && File.Exists(ignorePath))
                SetUnixPermissions(ignorePath, originalMode.Value);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_FailsClosedWhenNestedIgnoreFileIsUnreadable()
    {
        if (OperatingSystem.IsWindows())
            return;

        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        var nestedDir = Path.Combine(tempDir, "src");
        var ignorePath = Path.Combine(nestedDir, ".gitignore");
        UnixFileMode? originalMode = null;
        try
        {
            Directory.CreateDirectory(nestedDir);
            File.WriteAllText(Path.Combine(tempDir, "keep.py"), "print('keep')");
            File.WriteAllText(ignorePath, "secret.py\n");
            File.WriteAllText(Path.Combine(nestedDir, "secret.py"), "print('secret')");
            File.WriteAllText(Path.Combine(nestedDir, "keep_nested.py"), "print('nested keep')");
            originalMode = File.GetUnixFileMode(ignorePath);
            SetUnixPermissions(ignorePath, UnixFileMode.None);

            var indexer = new FileIndexer(tempDir);
            var result = indexer.ScanFilesDetailed();
            var files = result.Files
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(["keep.py"], files);
            Assert.Contains(result.Errors, error => error.Path == "src/.gitignore" && error.Message == "Could not read .gitignore.");
        }
        finally
        {
            if (originalMode.HasValue && File.Exists(ignorePath))
                SetUnixPermissions(ignorePath, originalMode.Value);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_RespectsRootAnchoredGitignorePatterns()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "root_only_dir"));
            Directory.CreateDirectory(Path.Combine(tempDir, "src"));
            Directory.CreateDirectory(Path.Combine(tempDir, "src", "root_only_dir"));
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "/root_only_dir/\n/secret.py\n");
            File.WriteAllText(Path.Combine(tempDir, "root_only_dir", "root.py"), "print('ignored root dir')");
            File.WriteAllText(Path.Combine(tempDir, "secret.py"), "print('ignored root file')");
            File.WriteAllText(Path.Combine(tempDir, "keep.py"), "print('kept root file')");
            File.WriteAllText(Path.Combine(tempDir, "src", "root_only_dir", "nested.py"), "print('kept nested dir')");
            File.WriteAllText(Path.Combine(tempDir, "src", "secret.py"), "print('kept nested file')");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "keep.py", "src/root_only_dir/nested.py", "src/secret.py"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_RespectsGlobstarPrefixPatternAtProjectRoot()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "nested"));
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "**/*.min.js\n");
            File.WriteAllText(Path.Combine(tempDir, "app.min.js"), "export const ignored = true;");
            File.WriteAllText(Path.Combine(tempDir, "nested", "lib.min.js"), "export const nestedIgnored = true;");
            File.WriteAllText(Path.Combine(tempDir, "app.js"), "export const kept = true;");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "app.js"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_RespectsGlobstarMiddlePatternWithZeroOrMoreDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "foo"));
            Directory.CreateDirectory(Path.Combine(tempDir, "foo", "deep"));
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "foo/**/bar.py\n");
            File.WriteAllText(Path.Combine(tempDir, "foo", "bar.py"), "print('ignored shallow')");
            File.WriteAllText(Path.Combine(tempDir, "foo", "deep", "bar.py"), "print('ignored deep')");
            File.WriteAllText(Path.Combine(tempDir, "foo", "keep.py"), "print('kept')");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "foo/keep.py"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_RespectsTrailingGlobstarWithoutIgnoringRootDirectoryItself()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "foo"));
            Directory.CreateDirectory(Path.Combine(tempDir, "foo", "nested"));
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "foo/**\n!foo/bar.py\n");
            File.WriteAllText(Path.Combine(tempDir, "foo", "bar.py"), "print('keep')");
            File.WriteAllText(Path.Combine(tempDir, "foo", "nested", "ignored.py"), "print('ignored')");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "foo/bar.py"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_RespectsTrailingGlobstarDirectoryPatternWithoutIgnoringRootDirectoryItself()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "foo"));
            Directory.CreateDirectory(Path.Combine(tempDir, "foo", "bar"));
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "foo/**/\n!foo/bar.py\n");
            File.WriteAllText(Path.Combine(tempDir, "foo", "bar.py"), "print('keep')");
            File.WriteAllText(Path.Combine(tempDir, "foo", "keep.py"), "print('keep')");
            File.WriteAllText(Path.Combine(tempDir, "foo", "bar", "ignored.py"), "print('ignored')");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "foo/bar.py", "foo/keep.py"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_TreatsNonSpecialDoubleStarAsSingleSegmentWildcard()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "dir"));
            Directory.CreateDirectory(Path.Combine(tempDir, "dir", "a"));
            Directory.CreateDirectory(Path.Combine(tempDir, "dir", "a", "x"));
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "dir/a**b.py\n");
            File.WriteAllText(Path.Combine(tempDir, "dir", "ab.py"), "print('ignored')");
            File.WriteAllText(Path.Combine(tempDir, "dir", "axxb.py"), "print('ignored')");
            File.WriteAllText(Path.Combine(tempDir, "dir", "a", "x", "b.py"), "print('kept')");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "dir/a/x/b.py"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_RespectsGitIgnoreCaseSettingFromRepository()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            RunGit(tempDir, "init");
            RunGit(tempDir, "config", "user.name", "CodeIndex Tests");
            RunGit(tempDir, "config", "user.email", "tests@example.com");
            RunGit(tempDir, "config", "core.ignorecase", "true");
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "FOO.py\n");
            File.WriteAllText(Path.Combine(tempDir, "foo.py"), "print('ignored')");
            File.WriteAllText(Path.Combine(tempDir, "keep.py"), "print('kept')");

            var indexer = new FileIndexer(tempDir, GitHelper.ResolveIgnoreCase(tempDir));
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "keep.py"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_SubdirectoryProjectRoot_RespectsAncestorGitignore()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        var projectRoot = Path.Combine(tempDir, "subproj");
        try
        {
            Directory.CreateDirectory(projectRoot);
            RunGit(tempDir, "init");
            RunGit(tempDir, "config", "user.name", "CodeIndex Tests");
            RunGit(tempDir, "config", "user.email", "tests@example.com");
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "subproj/ignored.py\n");
            File.WriteAllText(Path.Combine(projectRoot, "ignored.py"), "print('ignored')");
            File.WriteAllText(Path.Combine(projectRoot, "keep.py"), "print('kept')");

            var indexer = new FileIndexer(projectRoot, GitHelper.ResolveIgnoreCase(projectRoot), GitHelper.TryGetRepositoryRoot(projectRoot));
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(projectRoot, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(["keep.py"], files);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_SubdirectoryProjectRoot_RespectsAncestorGitignoreDirectoryRule()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        var projectRoot = Path.Combine(tempDir, "subproj");
        try
        {
            Directory.CreateDirectory(projectRoot);
            RunGit(tempDir, "init");
            RunGit(tempDir, "config", "user.name", "CodeIndex Tests");
            RunGit(tempDir, "config", "user.email", "tests@example.com");
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "subproj/\n");
            File.WriteAllText(Path.Combine(projectRoot, "app.py"), "print('ignored root dir')");

            var indexer = new FileIndexer(projectRoot, GitHelper.ResolveIgnoreCase(projectRoot), GitHelper.TryGetRepositoryRoot(projectRoot));
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(projectRoot, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Empty(files);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_ProjectRootNamedNodeModules_IsSkippedByDefaultDirectoryFilter()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        var projectRoot = Path.Combine(tempDir, "node_modules");
        try
        {
            Directory.CreateDirectory(projectRoot);
            File.WriteAllText(Path.Combine(projectRoot, "app.js"), "console.log('ignored root dir');");

            var indexer = new FileIndexer(projectRoot);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(projectRoot, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Empty(files);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_RespectsGitignoreBracketCharacterClassesAndRanges()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "[ab].cs\nfile[0-9].py\n");
            File.WriteAllText(Path.Combine(tempDir, "a.cs"), "class A { }");
            File.WriteAllText(Path.Combine(tempDir, "b.cs"), "class B { }");
            File.WriteAllText(Path.Combine(tempDir, "c.cs"), "class C { }");
            File.WriteAllText(Path.Combine(tempDir, "file1.py"), "print('ignored')");
            File.WriteAllText(Path.Combine(tempDir, "filex.py"), "print('kept')");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "c.cs", "filex.py"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_RespectsGitignoreNegatedBracketCharacterClass()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "[!a].cs\n");
            File.WriteAllText(Path.Combine(tempDir, "a.cs"), "class A { }");
            File.WriteAllText(Path.Combine(tempDir, "b.cs"), "class B { }");
            File.WriteAllText(Path.Combine(tempDir, "c.cs"), "class C { }");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "a.cs"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_RespectsGitignoreBracketCharacterClassWithLeadingLiteralRightBracket()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "[]].cs\n");
            File.WriteAllText(Path.Combine(tempDir, "].cs"), "class Ignored { }");
            File.WriteAllText(Path.Combine(tempDir, "keep.cs"), "class Kept { }");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "keep.cs"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_RespectsGitignoreAsciiPosixDigitBracketCharacterClass()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "[[:digit:]].py\n");
            File.WriteAllText(Path.Combine(tempDir, "1.py"), "print('ignored')");
            File.WriteAllText(Path.Combine(tempDir, "١.py"), "print('kept non-ascii digit')");
            File.WriteAllText(Path.Combine(tempDir, "a.py"), "print('kept')");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "a.py", "١.py"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_RespectsGitignoreAsciiPosixUpperBracketCharacterClass()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "[[:upper:]].cs\n");
            File.WriteAllText(Path.Combine(tempDir, "A.cs"), "class Ignored { }");
            File.WriteAllText(Path.Combine(tempDir, "É.cs"), "class KeptNonAscii { }");
            File.WriteAllText(Path.Combine(tempDir, "keep.cs"), "class Kept { }");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "keep.cs", "É.cs"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_RespectsGitignorePosixPunctBracketCharacterClass()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "[[:punct:]].cs\n");
            File.WriteAllText(Path.Combine(tempDir, "!.cs"), "class Ignored { }");
            File.WriteAllText(Path.Combine(tempDir, "a.cs"), "class Kept { }");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "a.cs"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_RespectsGitignoreNegatedBracketCharacterClassWithLeadingLiteralRightBracket()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "[!]].cs\n");
            File.WriteAllText(Path.Combine(tempDir, "].cs"), "class Kept { }");
            File.WriteAllText(Path.Combine(tempDir, "a.cs"), "class Ignored { }");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "].cs"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_RespectsGitignoreEscapedLiteralCharacters()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "foo\\ bar.py\nliteral\\[name\\].js\n\\#literal.txt\n\\!important.cs\n");
            File.WriteAllText(Path.Combine(tempDir, "foo bar.py"), "print('ignored')");
            File.WriteAllText(Path.Combine(tempDir, "literal[name].js"), "export const ignored = true;");
            File.WriteAllText(Path.Combine(tempDir, "#literal.txt"), "ignored");
            File.WriteAllText(Path.Combine(tempDir, "!important.cs"), "class Ignored { }");
            File.WriteAllText(Path.Combine(tempDir, "keep.py"), "print('kept')");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles()
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "keep.py"], files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFilesDetailed_SkipsMalformedIgnoreRulesWithoutAborting()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, ".gitignore"), "[z-a].py\n[!].cs\n[a.py\n[!a\n[^\n[\n[]\nignored.py\n");
            File.WriteAllText(Path.Combine(tempDir, "[a.py"), "print('kept malformed literal')");
            File.WriteAllText(Path.Combine(tempDir, "ignored.py"), "print('ignored')");
            File.WriteAllText(Path.Combine(tempDir, "keep.py"), "print('kept')");

            var indexer = new FileIndexer(tempDir);
            var scanResult = indexer.ScanFilesDetailed();
            var files = scanResult.Files
                .Select(path => Path.GetRelativePath(tempDir, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.Equal([".gitignore", "[a.py", "keep.py"], files);
            Assert.Equal(7, scanResult.Errors.Count);
            Assert.All(scanResult.Errors, error => Assert.Contains(".gitignore:", error.Path, StringComparison.Ordinal));
            Assert.All(scanResult.Errors, error => Assert.Contains("Invalid ignore rule skipped", error.Message, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanFiles_IncludesModernNodeModuleExtensions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "index.mjs"), "export const run = () => {};");
            File.WriteAllText(Path.Combine(tempDir, "cli.cjs"), "module.exports = {};");
            File.WriteAllText(Path.Combine(tempDir, "types.cts"), "export type Config = {};");
            File.WriteAllText(Path.Combine(tempDir, "types.d.mts"), "export interface Config {}");
            File.WriteAllText(Path.Combine(tempDir, "notes.txt"), "ignored");

            var indexer = new FileIndexer(tempDir);
            var files = indexer.ScanFiles().Select(Path.GetFileName).OrderBy(name => name).ToList();

            Assert.Equal(["cli.cjs", "index.mjs", "types.cts", "types.d.mts"], files);
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
    public void GetFamilyScopeKey_MarkerlessRootUsesTopLevelSubtreeScope()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "src"));
            Directory.CreateDirectory(Path.Combine(tempDir, "generated"));

            var srcFile = Path.Combine(tempDir, "src", "Api.Part1.cs");
            var generatedFile = Path.Combine(tempDir, "generated", "Api.Part2.cs");
            File.WriteAllText(srcFile, "public partial class Api {}");
            File.WriteAllText(generatedFile, "public partial class Api {}");

            var indexer = new FileIndexer(tempDir);

            Assert.Equal("src", indexer.GetFamilyScopeKey(srcFile, "csharp"));
            Assert.Equal("generated", indexer.GetFamilyScopeKey(generatedFile, "csharp"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetFamilyScopeKey_MarkerlessRootLevelFilesShareRootScope()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            var firstFile = Path.Combine(tempDir, "Api.Part1.cs");
            var secondFile = Path.Combine(tempDir, "Api.Part2.cs");
            File.WriteAllText(firstFile, "public partial class Api {}");
            File.WriteAllText(secondFile, "public partial class Api {}");

            var indexer = new FileIndexer(tempDir);

            Assert.Equal(".", indexer.GetFamilyScopeKey(firstFile, "csharp"));
            Assert.Equal(".", indexer.GetFamilyScopeKey(secondFile, "csharp"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetFamilyScopeKey_MultipleProjectMarkersInOneDirectoryUseNarrowerSubtreeScope()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codeindex_test_{Guid.NewGuid():N}");
        try
        {
            var srcDir = Path.Combine(tempDir, "src");
            Directory.CreateDirectory(Path.Combine(srcDir, "ProjA"));
            Directory.CreateDirectory(Path.Combine(srcDir, "ProjB"));
            File.WriteAllText(Path.Combine(srcDir, "ProjectA.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(srcDir, "ProjectB.csproj"), "<Project />");

            var projAFile = Path.Combine(srcDir, "ProjA", "Api.Part1.cs");
            var projBFile = Path.Combine(srcDir, "ProjB", "Api.Part1.cs");
            var ambiguousFile = Path.Combine(srcDir, "Api.Part1.cs");
            File.WriteAllText(projAFile, "public partial class Api {}");
            File.WriteAllText(projBFile, "public partial class Api {}");
            File.WriteAllText(ambiguousFile, "public partial class Api {}");

            var indexer = new FileIndexer(tempDir);

            Assert.Equal("src/ProjA", indexer.GetFamilyScopeKey(projAFile, "csharp"));
            Assert.Equal("src/ProjB", indexer.GetFamilyScopeKey(projBFile, "csharp"));
            Assert.Equal("src/__file__/Api.Part1.cs", indexer.GetFamilyScopeKey(ambiguousFile, "csharp"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
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

    private static string RunGit(string workDir, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process / gitプロセスの起動に失敗");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr.Trim()}");

        return stdout;
    }

    [UnsupportedOSPlatform("windows")]
    private static void SetUnixPermissions(string path, UnixFileMode mode)
    {
        File.SetUnixFileMode(path, mode);
    }
}
