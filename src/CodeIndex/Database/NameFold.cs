using System.Text;

namespace CodeIndex.Database;

/// <summary>
/// Stable NFKC + invariant-lower fold for `--exact` symbol/reference name matching (#86).
/// SQLite's built-in NOCASE collation only folds ASCII, so common symbol-name pairs
/// like `Ä` / `ä` and fullwidth `Ｒｕｎ` / `Run` would silently miss each other. This
/// fold closes the common cases: Latin-1 accents, fullwidth/halfwidth, ligatures
/// (`ﬁ` → `fi`), superscripts, compatibility Kana.
///
/// NOT full Unicode CaseFold. `string.ToLowerInvariant()` is close but not identical
/// to the Unicode casefolding algorithm, so a few edge-case pairs still diverge —
/// the classic example is Turkish dotted-I: `"İ".Normalize(FormKC).ToLowerInvariant()`
/// can return `"i\u0307"` (i + combining dot above) on some runtimes instead of `"i"`,
/// so `İ`-vs-`i` does not match through this fold. Greek final sigma (`Σ`/`ς`/`σ`)
/// and some combining-mark normalization corners can also slip. Callers who need
/// guaranteed Unicode-correct case folding should pass the exact casing found during
/// indexing. See #96 for the follow-up on a true CaseFold key.
///
/// Stability contract: the same input MUST always fold to the same output across
/// OS / process boundaries, because the writer populates the column at index time
/// and the reader folds the query at lookup time — any drift would produce silent
/// misses. FormKC is stable per Unicode version; `ToLowerInvariant` uses the
/// .NET-embedded invariant tables.
///
/// `--exact` シンボル名マッチ用の NFKC + invariant-lower 折り畳み (#86)。SQLite の
/// NOCASE は ASCII 限定のため `Ä`/`ä` や全角/半角 `Ｒｕｎ`/`Run` を取りこぼす。この
/// fold は Latin-1 アクセント、全角/半角、合字、互換 Kana 等の現実的な casing を
/// カバーする。ただし完全な Unicode CaseFold ではない: トルコ語の `İ`/`i`、ギリシャ
/// 語 final sigma (`Σ`/`ς`/`σ`) などのエッジケースは拾えない（詳細は #96）。
/// 安定性: writer と reader が同じ関数で同じ入力に同じ出力を返す必要があるため、
/// OS / プロセスに依存しない決定的変換である FormKC + ToLowerInvariant を使う。
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
        // canonical form. Then ToLowerInvariant folds most ASCII + Latin-1 case
        // (Ä → ä) without locale surprises. Does NOT implement full Unicode
        // CaseFold — see the class doc for the edge cases (Turkish İ, Greek
        // final sigma) that still need exact casing. #96 tracks the full fix.
        // FormKC + ToLowerInvariant で現実的な casing を畳む。完全 CaseFold は #96。
        return name.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
    }
}
