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
        [".h"]      = "c",
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
    };

    // Directories to skip (exact match) / スキップするディレクトリ（完全一致）
    private static readonly HashSet<string> SkipDirs = new(StringComparer.Ordinal)
    {
        ".git", ".svn", ".hg",
        "node_modules", "__pycache__", ".pytest_cache",
        "venv", ".venv", "env",
        "dist", "build", ".build", "out",
        ".next", ".nuxt",
        ".idea", ".vscode",
        "coverage", "vendor",
    };

    // Files to skip (exact match) / スキップするファイル名（完全一致）
    private static readonly HashSet<string> SkipFiles = new(StringComparer.Ordinal)
    {
        ".DS_Store", "Thumbs.db",
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml",
    };

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
    /// Build a FileRecord from the given file path.
    /// 指定パスからFileRecordを構築する。
    /// </summary>
    public FileRecord BuildRecord(string absolutePath)
    {
        var relativePath = Path.GetRelativePath(_projectRoot, absolutePath);
        var info = new FileInfo(absolutePath);

        // Read content with UTF-8, replacing invalid bytes
        // UTF-8で読み込み、不正バイトは置換文字で処理
        var content = File.ReadAllText(absolutePath, new UTF8Encoding(false, false));
        var lines = content.Split('\n');
        var snippet = content.Length > 2000 ? content[..2000] : content;
        var checksum = ComputeChecksum(content);

        return new FileRecord
        {
            Path = relativePath.Replace('\\', '/'),
            Lang = DetectLanguage(absolutePath),
            Size = info.Length,
            Lines = lines.Length,
            Snippet = snippet,
            Checksum = checksum,
            Modified = info.LastWriteTimeUtc,
        };
    }

    /// <summary>
    /// Compute SHA256 checksum of the content.
    /// コンテンツのSHA256チェックサムを算出する。
    /// </summary>
    private static string ComputeChecksum(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
