---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R `$` references now handle backtick receiver names** — member accesses such as `` `reactive data`$value `` and `` `reactive data`$`has space` `` are indexed as qualified references.

## 日本語

- **R の `$` 参照がバッククォート付き receiver 名に対応しました** — `` `reactive data`$value `` や `` `reactive data`$`has space` `` を qualified reference として索引します。
