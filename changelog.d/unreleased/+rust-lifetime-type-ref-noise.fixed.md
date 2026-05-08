---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust lifetime names no longer appear as type references** — lifetime markers such as `'a` are skipped while preserving the surrounding real types.

## 日本語

- **Rust lifetime 名が type reference として出ないようになりました** — `'a` などの lifetime marker を除外しつつ、周辺の実型は保持します。
