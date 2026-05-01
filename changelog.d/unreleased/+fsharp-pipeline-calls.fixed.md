---
category: added
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - README.md
---

## English

- **F# pipeline calls are now indexed for graph queries** — `references`, `callers`, and `callees` now capture F# parenthesized and pipeline call sites such as `List.map`, `List.filter`, and `||>` chains, so common functional pipelines are searchable instead of falling back to text search.

## 日本語

- **F# の pipeline 呼び出しが graph query でも索引されるようになりました** — `references`、`callers`、`callees` が F# の括弧付き呼び出しと pipeline 呼び出し（`List.map`、`List.filter`、`||>` 連鎖など）を拾うようになり、よくある関数型パイプラインをテキスト検索に頼らず辿れるようになりました。
