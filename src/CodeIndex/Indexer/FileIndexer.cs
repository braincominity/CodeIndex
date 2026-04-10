using System.Security.Cryptography;
using System.Text;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

/// <summary>
/// Scans directories for source files and builds FileRecords.
/// ディレクトリを走査してソースファイルからFileRecordを構築する。
/// </summary>
public class FileIndexer
{
    // Extension-to-language mapping / 拡張子→言語名マッピング
    private static readonly Dictionary<string, string> LangMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".py"]     = "python",
        [".js"]     = "javascript",
        [".ts"]     = "typescript",
        [".jsx"]    = "javascript",
        [".tsx"]    = "typescript",
        [".rb"]     = "ruby",
        [".go"]     = "go",
        [".rs"]     = "rust",
        [".java"]   = "java",
        [".kt"]     = "kotlin",
        [".swift"]  = "swift",
        [".c"]      = "c",
        [".cpp"]    = "cpp",
        [".cc"]     = "cpp",
        [".cxx"]    = "cpp",
        [".h"]      = "c",       // Could be C or C++; defaults to C for symbol extraction
        [".hpp"]    = "cpp",
        [".hxx"]    = "cpp",
        [".cs"]     = "csharp",
        [".php"]    = "php",
        [".sh"]     = "shell",
        [".sql"]    = "sql",
        [".md"]     = "markdown",
        [".yaml"]   = "yaml",
        [".yml"]    = "yaml",
        [".json"]   = "json",
        [".toml"]   = "toml",
        [".html"]   = "html",
        [".css"]    = "css",
        [".scss"]   = "css",
        [".vue"]    = "vue",
        [".svelte"] = "svelte",
        [".tf"]     = "terraform",
        [".dart"]   = "dart",
        [".scala"]  = "scala",
        [".sc"]     = "scala",
        [".r"]      = "r",
        [".R"]      = "r",
        [".ex"]     = "elixir",
        [".exs"]    = "elixir",
        [".lua"]    = "lua",
    };

    // Directories to skip (case-insensitive for cross-platform) / スキップするディレクトリ（クロスプラットフォーム対応で大文字小文字を区別しない）
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg",
        "node_modules", "__pycache__", ".pytest_cache",
        "venv", ".venv", "env",
        "dist", "build", ".build", "out",
        "bin", "obj",                   // .NET build outputs / .NETビルド出力
        "target",                       // Rust/Java/Maven build output / Rust/Java/Mavenビルド出力
        ".gradle",                      // Gradle cache / Gradleキャッシュ
        ".next", ".nuxt",
        ".idea", ".vscode",
        "coverage", "vendor",
    };

    // Files to skip (case-insensitive for cross-platform consistency with SkipDirs)
    // スキップするファイル名（SkipDirsと同様にクロスプラットフォーム対応で大文字小文字を区別しない）
    private static readonly HashSet<string> SkipFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store", "Thumbs.db",
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml",
        "Gemfile.lock", "Cargo.lock", "composer.lock", "poetry.lock", "bun.lockb",
    };

    // Maximum file size to index (10 MB) / インデックス対象の最大ファイルサイズ (10 MB)
    private const long MaxFileSize = 10 * 1024 * 1024;

    private readonly string _projectRoot;

    public FileIndexer(string projectRoot)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
    }

    /// <summary>
    /// Try to detect the language from a file extension.
    /// ファイル拡張子から言語を検出する。
    /// </summary>
    public static string? DetectLanguage(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return LangMap.TryGetValue(ext, out var lang) ? lang : null;
    }

    /// <summary>
    /// Enumerate all indexable files under the project root.
    /// プロジェクトルート以下のインデックス対象ファイルを列挙する。
    /// </summary>
    public IReadOnlyList<string> ScanFiles()
    {
        var files = new List<string>();
        ScanDirectory(_projectRoot, files);
        return files;
    }

    private void ScanDirectory(string dir, List<string> results)
    {
        // Check for skip directories / スキップ対象ディレクトリかチェック
        var dirName = Path.GetFileName(dir);
        if (SkipDirs.Contains(dirName))
            return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var fileName = Path.GetFileName(file);

                // Skip excluded file names / 除外ファイル名をスキップ
                if (SkipFiles.Contains(fileName))
                    continue;

                // Only include files with a known extension / 既知の拡張子のみ含める
                var ext = Path.GetExtension(file);
                if (LangMap.ContainsKey(ext))
                    results.Add(file);
            }

            foreach (var subDir in Directory.EnumerateDirectories(dir))
            {
                ScanDirectory(subDir, results);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip inaccessible directories / アクセス不可ディレクトリはスキップ
        }
        catch (IOException)
        {
            // Skip on I/O errors / I/Oエラー時はスキップ
        }
    }

    /// <summary>
    /// Build a FileRecord and return file content (avoids reading the file twice).
    /// FileRecordを構築しファイル内容も返す（二重読み込み防止）。
    /// </summary>
    public (FileRecord record, string content, string? warning) BuildRecord(string absolutePath)
    {
        var relativePath = Path.GetRelativePath(_projectRoot, absolutePath);
        var info = new FileInfo(absolutePath);

        // Skip files exceeding size limit to avoid OutOfMemoryException
        // OOM防止のためサイズ上限を超えるファイルをスキップ
        if (info.Length > MaxFileSize)
            throw new InvalidOperationException($"File too large ({info.Length / 1024 / 1024} MB > {MaxFileSize / 1024 / 1024} MB limit)");

        // Read raw bytes and decode UTF-8; detect invalid sequences
        // 生バイト読み込み後UTF-8デコード、不正シーケンスを検出
        var bytes = File.ReadAllBytes(absolutePath);

        // Compute checksum from raw bytes to avoid re-encoding the string (~10MB saved for large files)
        // 文字列の再エンコードを回避するためraw bytesからチェックサムを算出（大ファイルで約10MB節約）
        var checksum = ComputeChecksum(bytes);

        string content;
        string? warning = null;
        try
        {
            content = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Fall back to replacement mode but warn / 置換モードにフォールバックし警告
            content = new UTF8Encoding(false, throwOnInvalidBytes: false).GetString(bytes);
            warning = $"{relativePath}: contains invalid UTF-8 bytes (replaced with U+FFFD)";
        }
        // Normalize line endings to LF / 改行をLFに正規化
        content = content.Replace("\r\n", "\n").Replace("\r", "\n");
        // Accurate line count: ignore trailing newline / 正確な行数: 末尾改行を無視
        var lines = content.EndsWith('\n')
            ? content[..^1].Split('\n')
            : content.Split('\n');

        var record = new FileRecord
        {
            Path = relativePath.Replace('\\', '/'),
            Lang = DetectLanguage(absolutePath),
            Size = info.Length,
            Lines = lines.Length,
            Checksum = checksum,
            Modified = info.LastWriteTimeUtc,
        };

        return (record, content, warning);
    }

    /// <summary>
    /// Compute SHA256 checksum from raw file bytes.
    /// ファイルのraw bytesからSHA256チェックサムを算出する。
    /// </summary>
    private static string ComputeChecksum(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
