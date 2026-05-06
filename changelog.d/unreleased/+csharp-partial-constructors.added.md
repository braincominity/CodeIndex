---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **C# partial constructors are now indexed as function symbols** — C# 14 `public partial Widget();` and `public partial Widget() { }` declarations now appear in `symbols`, `definition`, and `outline` results.

## 日本語

- **C# の partial constructor を function シンボルとして索引するようになりました** — C# 14 の `public partial Widget();` や `public partial Widget() { }` 宣言が `symbols`、`definition`、`outline` の結果に現れるようになりました。
