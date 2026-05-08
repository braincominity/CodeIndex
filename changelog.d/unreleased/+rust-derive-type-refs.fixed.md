---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust derive traits are now indexed as type references** — `#[derive(Debug, Clone, serde::Serialize)]` now exposes the derived trait names to reference search and inspection.

## 日本語

- **Rust derive trait を type reference としてインデックスするようになりました** — `#[derive(Debug, Clone, serde::Serialize)]` の derive 対象 trait 名を reference search や inspect から辿れます。
