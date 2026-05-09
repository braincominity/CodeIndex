---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R `NAMESPACE` native library directives now keep package references searchable** — `useDynLib(pkg, ...)` emits the package name as a reference while suppressing the directive helper call.

## 日本語

- **R の `NAMESPACE` ネイティブライブラリ directive がパッケージ参照を検索に残すようになりました** — `useDynLib(pkg, ...)` は directive helper call を抑止しつつ、パッケージ名を参照として記録します。
