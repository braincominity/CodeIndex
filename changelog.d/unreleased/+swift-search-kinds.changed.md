---
category: changed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **Swift `typealias` and `associatedtype` symbols now use dedicated kinds** — Swift alias declarations no longer get folded into `import`, so `symbols`, `definition`, and `--kind` filters can separate alias queries from real import entries.

## 日本語

- **Swift の `typealias` と `associatedtype` を専用 kind で扱うようになりました** — Swift の別名宣言が `import` に混ざらなくなり、`symbols`、`definition`、`--kind` フィルタで別名検索と実際の import を分離して扱えます。
