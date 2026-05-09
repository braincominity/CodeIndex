---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R backtick-named function calls now appear in reference search** — calls such as `` `plot-model`(x) `` and `` `%||%`(x, y) `` are emitted as call references.

## 日本語

- **R のバッククォート名付き関数呼び出しが参照検索に出るようになりました** — `` `plot-model`(x) `` や `` `%||%`(x, y) `` のような呼び出しを call 参照として記録します。
