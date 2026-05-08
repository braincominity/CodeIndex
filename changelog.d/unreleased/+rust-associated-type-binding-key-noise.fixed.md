---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust associated type binding keys no longer appear as type references** — `Future<Output = User>` keeps the real `Future`/`User` dependencies without indexing `Output` as a target type.

## 日本語

- **Rust associated type binding の key が type reference として出なくなりました** — `Future<Output = User>` では実依存の `Future`/`User` を残し、`Output` は対象型として索引しません。
