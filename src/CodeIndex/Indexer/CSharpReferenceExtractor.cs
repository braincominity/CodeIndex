using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class CSharpReferenceExtractor
{
    // C# constructor chain initializer: `public A() : this(0)` / `public B() : base(42)`
    // C# コンストラクタ連鎖イニシャライザ
    private static readonly Regex CtorChainRegex = new(@":\s*(?<kind>this|base)\s*\(", RegexOptions.Compiled);

    public static void EmitCtorChainReferences(
        string preparedLine,
        IReadOnlyList<SymbolRecord> enclosingTypeCandidates,
        IReadOnlyList<SymbolRecord> containerCandidates,
        string[] structuralLines,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var chainMatches = CtorChainRegex.Matches(preparedLine);
        if (chainMatches.Count == 0)
            return;

        var enclosingType = ReferenceExtractor.FindInnermostClassLike(enclosingTypeCandidates, lineNumber);
        if (enclosingType == null)
            return;

        // For cross-line initializers such as:
        //   public A(int x, int y)
        //       : this(x, 0)
        //   { }
        // the chain line precedes the body, so the inner-most "body-covering" container lookup
        // returns the class rather than the constructor. Fall back to a declaration-to-body-end
        // lookup so the reference is attributed to the constructor that owns the chain.
        // クロス行イニシャライザでは body よりも前に連鎖行が現れるため、body 範囲のみで
        // 判定すると外側クラスが選ばれる。宣言〜body 終端の範囲で探し直す。
        var chainContainer = container;
        if (chainContainer == null || chainContainer.Kind != "function")
        {
            chainContainer = FindDeclarationRangeFunction(containerCandidates, lineNumber) ?? chainContainer;
        }

        foreach (Match match in chainMatches)
        {
            var kindToken = match.Groups["kind"].Value;
            string? target;
            if (kindToken == "this")
            {
                target = enclosingType.Name;
            }
            else
            {
                // `base(...)` needs the base type from the enclosing class's signature.
                // SymbolRecord.Signature only captures the first declaration line, so multi-line
                // base-lists (e.g. `class Child\n    : Parent`) lose the `: Parent` continuation.
                // Reconstruct the joined header up to the first `;` or `{` from structuralLines.
                // `base(...)` は外側クラスのシグネチャから基底型を解析する必要がある。
                // SymbolRecord.Signature は宣言 1 行目しか持たないので複数行 base-list が欠落する。
                // structuralLines から最初の `;` / `{` までを連結し直して渡す。
                var (_, _, headerText) = ReferenceExtractor.CollectCSharpRecordHeader(
                    structuralLines,
                    enclosingType.StartLine);
                target = ReferenceExtractor.ParseCSharpBaseType(headerText);
                if (string.IsNullOrWhiteSpace(target))
                    target = ReferenceExtractor.ParseCSharpBaseType(enclosingType.Signature);
                if (string.IsNullOrWhiteSpace(target))
                    continue;
            }

            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                target!,
                match.Groups["kind"].Index,
                "call",
                context,
                lineNumber,
                chainContainer);
        }
    }

    private static SymbolRecord? FindDeclarationRangeFunction(
        IReadOnlyList<SymbolRecord> candidates,
        int lineNumber)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Kind != "function")
                continue;
            if (candidate.StartLine <= lineNumber && candidate.BodyEndLine!.Value >= lineNumber)
                return candidate;
        }

        return null;
    }
}
