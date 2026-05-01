---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Go grouped `type`, `const`, and `var` blocks are now indexed** — symbol extraction now records declarations inside `type (...)`, `const (...)`, and `var (...)` blocks, so searches can find grouped Go definitions instead of missing them entirely.

## 日本語

- **Go の grouped な `type` / `const` / `var` ブロックもインデックスされるようになりました** — `type (...)` / `const (...)` / `var (...)` の中の宣言もシンボル抽出で記録するため、group 化された Go 定義を検索でき、完全に見落とされることがなくなります。
