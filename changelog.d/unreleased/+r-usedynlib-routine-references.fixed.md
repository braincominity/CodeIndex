---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R NAMESPACE `useDynLib()` routine names now surface in reference search** — native entries such as `routine_a` and backtick-escaped routine names are indexed alongside the package reference.

## 日本語

- **R NAMESPACE の `useDynLib()` routine 名が参照検索に出るようになりました** — `routine_a` やバッククォート付き routine 名のような native entry を package reference とあわせて索引します。
