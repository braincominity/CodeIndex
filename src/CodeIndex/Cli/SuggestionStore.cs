using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodeIndex.Models;

namespace CodeIndex.Cli;

/// <summary>
/// Reads and writes improvement suggestions to .cdidx/suggestions.json.
/// Provides deduplication via SHA256 hash of (category + language + normalized description).
/// 改善提案を .cdidx/suggestions.json に読み書きする。
/// (category + language + 正規化済み description) のSHA256ハッシュで重複排除する。
/// </summary>
public class SuggestionStore
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Create a new SuggestionStore for the given .cdidx directory.
    /// 指定した .cdidx ディレクトリ用の SuggestionStore を作成する。
    /// </summary>
    /// <param name="cdidxDir">Path to the .cdidx directory / .cdidxディレクトリのパス</param>
    public SuggestionStore(string cdidxDir)
    {
        _filePath = Path.Combine(cdidxDir, "suggestions.json");
    }

    /// <summary>
    /// Compute the dedup hash for a suggestion.
    /// The hash is derived from category, language (lowered), and description (trimmed + lowered).
    /// This ensures that trivially different phrasings (e.g. different casing) produce the same hash.
    /// 提案の重複排除用ハッシュを計算する。
    /// category、language（小文字化）、description（trim + 小文字化）から導出する。
    /// 些細な表現差（大小文字等）で同じハッシュが生成されるようにする。
    /// </summary>
    public static string ComputeHash(string category, string? language, string description)
    {
        // Normalize: category is already constrained to enum values,
        // language is lowered, description is trimmed and lowered.
        // 正規化: category は enum 値に制約済み、
        // language は小文字化、description は trim + 小文字化。
        var normalized = $"{category}|{(language ?? "").ToLowerInvariant()}|{description.Trim().ToLowerInvariant()}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Try to add a suggestion. Returns false if a suggestion with the same hash already exists.
    /// 提案の追加を試みる。同一ハッシュの提案が既にあれば false を返す。
    /// </summary>
    public bool TryAdd(SuggestionRecord record)
    {
        var existing = LoadAll();
        if (existing.Any(s => s.Hash == record.Hash))
            return false;

        existing.Add(record);
        Save(existing);
        return true;
    }

    /// <summary>
    /// Load all suggestions from disk. Returns an empty list if the file
    /// does not exist, is empty, or contains invalid JSON.
    /// ディスクから全提案を読み込む。ファイルが存在しない、空、
    /// または不正なJSONの場合は空リストを返す。
    /// </summary>
    public List<SuggestionRecord> LoadAll()
    {
        if (!File.Exists(_filePath))
            return new List<SuggestionRecord>();

        try
        {
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
                return new List<SuggestionRecord>();

            return JsonSerializer.Deserialize<List<SuggestionRecord>>(json, s_readOptions)
                   ?? new List<SuggestionRecord>();
        }
        catch (JsonException)
        {
            // Corrupt file — preserve as .bak so history is not lost,
            // then treat as empty for the current operation.
            // 壊れたファイル — 履歴を失わないよう .bak として保存し、
            // 今回の操作では空として扱う。
            PreserveCorruptFile();
            return new List<SuggestionRecord>();
        }
        catch (IOException)
        {
            // File access error — treat as empty / ファイルアクセスエラー — 空として扱う
            return new List<SuggestionRecord>();
        }
    }

    /// <summary>
    /// Mark a suggestion as submitted to GitHub by updating its URL and flag.
    /// 提案を GitHub 送信済みとしてマークし、URL とフラグを更新する。
    /// </summary>
    public void MarkSubmitted(string hash, string issueUrl)
    {
        var all = LoadAll();
        var record = all.FirstOrDefault(s => s.Hash == hash);
        if (record == null)
            return;

        record.SubmittedToGitHub = true;
        record.GitHubIssueUrl = issueUrl;
        Save(all);
    }

    /// <summary>
    /// Write the full suggestion list to disk atomically.
    /// Uses write-to-temp-and-rename to prevent partial writes from
    /// corrupting the main file. Creates the directory if needed.
    /// 提案リスト全体をアトミックにディスクへ書き込む。
    /// 部分書き込みでメインファイルを壊さないよう、一時ファイルに書いてから
    /// リネームする。必要に応じてディレクトリを作成する。
    /// </summary>
    private void Save(List<SuggestionRecord> records)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Write to a temp file first, then atomically replace the target.
        // This ensures the main file is never left in a partial/corrupt state.
        // まず一時ファイルに書き、その後アトミックにターゲットを置換する。
        // メインファイルが部分書き込み/破損状態にならないことを保証する。
        var tempPath = _filePath + ".tmp";
        var json = JsonSerializer.Serialize(records, s_jsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    /// <summary>
    /// Preserve a corrupt suggestions file by renaming it to .bak.
    /// This prevents silent data loss — the corrupt file can be inspected
    /// or recovered manually.
    /// 破損した suggestions ファイルを .bak にリネームして保存する。
    /// サイレントなデータ消失を防ぐ — 破損ファイルは手動で検査・復旧できる。
    /// </summary>
    private void PreserveCorruptFile()
    {
        try
        {
            var backupPath = _filePath + ".bak";
            if (File.Exists(_filePath))
                File.Move(_filePath, backupPath, overwrite: true);
        }
        catch
        {
            // Best-effort — if we can't rename, continue with empty list.
            // ベストエフォート — リネームできなければ空リストで続行。
        }
    }
}
