---
category: changed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Swift symbol search now indexes extension declarations and escaped function names** — `symbols`/`search` can now find Swift `extension` targets and backtick-escaped function identifiers (for example, ``func `repeat`()``).

## 日本語

- **Swift シンボル検索で extension 宣言とエスケープ関数名を索引化** — `symbols` / `search` が Swift の `extension` 対象とバッククォート付き関数名（例: ``func `repeat`()``）を検出できるようになりました。
