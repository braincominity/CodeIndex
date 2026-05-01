---
category: fixed
issues: []
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **C++ nested namespaces now keep their qualified names in symbol search** — `symbols` now preserves declarations like `namespace outer::inner {}` as `outer::inner`, which makes nested namespace lookups and container attribution work directly for C++ projects.

## 日本語

- **C++ のネストした namespace がシンボル検索で完全修飾名のまま扱われるようになりました** — `symbols` が `namespace outer::inner {}` を `outer::inner` として保持するため、C++ プロジェクトでネストした namespace の検索と container 属性付けをそのまま行えます。
