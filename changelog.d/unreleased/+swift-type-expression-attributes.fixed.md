---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift type-expression attributes are no longer indexed as types** — `@retroactive`, `@escaping`, and `@Sendable` in conformance and function-type positions are skipped while preserving the real referenced types.

## 日本語

- **Swift の型式内属性を型として index しないようにしました** — conformance や関数型位置の `@retroactive` / `@escaping` / `@Sendable` を除外しつつ、実際に参照される型は保持します。
