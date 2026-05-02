---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R namespace references now emit reference edges** — `pkg::fun` and `pkg:::fun` are now indexed as `reference` rows, so package-qualified symbols remain searchable even when they are not invoked as calls.

## 日本語

- **R の namespace 参照が reference edge として出るようになりました** — `pkg::fun` と `pkg:::fun` を `reference` 行として索引するため、package 修飾された symbol も call されていない場合に検索しやすくなります。
