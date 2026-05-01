---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **TypeScript type-query extraction now handles `typeof import(...)` and deeper multiline continuations** - follow-up coverage from PR #1239 now keeps walking through import-type wrappers and continuation lines inside nested type expressions, so more real-world `references` / `impact` edges surface without mistaking runtime `typeof` usage for type references.

## 日本語

- **TypeScript の型クエリ抽出が `typeof import(...)` と、より深い多行継続を扱えるようになりました** - PR #1239 の follow-up として、import 型ラッパーや入れ子の型式内の継続行までたどるようにし、実際の `references` / `impact` エッジを増やしつつ、ランタイムの `typeof` を型参照と誤認しにくくしています。
