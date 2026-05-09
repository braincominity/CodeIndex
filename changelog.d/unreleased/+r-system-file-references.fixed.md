---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R `system.file()` calls now surface in reference search** — positional resource path segments and `package =` values are indexed as references.

## 日本語

- **R の `system.file()` 呼び出しが参照検索に出るようになりました** — positional resource path segment と `package =` 値を reference として索引します。
