using System.Text;

namespace CodeIndex.Database;

/// <summary>
/// Stable Unicode-aware fold for `--exact` symbol/reference name matching (#86).
/// SQLite's built-in NOCASE collation only folds ASCII, so symbol names containing
/// accented or wide characters (e.g. `Ä` / `ä`, fullwidth `Ｒｕｎ`) would silently
/// miss each other. Folded values are stored in `name_folded` columns and indexed
/// so `--exact` queries can match on equality (`s.name_folded = @qFolded`) while
/// staying SARGable.
///
/// Stability contract: the same input MUST always fold to the same output across
/// OS / process boundaries, because the writer populates the column at index time
/// and the reader folds the query at lookup time — any drift would produce silent
/// misses. Use <see cref="string.Normalize(NormalizationForm)"/> with `FormKC`
/// (compatibility composition, so fullwidth/halfwidth variants collapse) followed
/// by <see cref="string.ToLowerInvariant"/> (culture-independent case fold).
///
/// `--exact` シンボル名マッチ用の Unicode 折り畳みヘルパ (#86)。SQLite の NOCASE は
/// ASCII しか折り畳まないため、アクセント付き文字や全角文字を含むシンボル名を
/// 取りこぼしていた。折り畳み値は `name_folded` 列に保存しインデックスを貼る。
/// 安定性: writer は index 時、reader は query 時に同じ関数を呼ぶ必要があるため、
/// OS / プロセスに依存しない決定的変換であること。FormKC 正規化 + 不変文化の
/// 小文字化で両方とも満たす。
/// </summary>
public static class NameFold
{
    /// <summary>
    /// Fold a symbol name for `--exact` equality comparison. Returns null for
    /// null input so legacy / missing values stay distinguishable.
    /// </summary>
    public static string? Fold(string? name)
    {
        if (name is null) return null;
        // FormKC: compatibility decomposition + canonical recomposition.
        // Collapses fullwidth / halfwidth, ligatures (ﬁ → fi), superscripts,
        // compatibility Kana, and precomposed / decomposed accents to a single
        // canonical form. Then ToLowerInvariant folds ASCII and full-Unicode
        // case together (Ä → ä, İ → i̇, etc.) without locale surprises.
        // FormKC で互換分解 + 正準合成し、ToLowerInvariant で大文字小文字を畳む。
        return name.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
    }
}
