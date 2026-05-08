---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift tuple and function type labels no longer appear as type references** — labels in shapes such as `(x: Coordinate, y: Coordinate)` are ignored while the actual element types remain indexed.

## 日本語

- **Swift の tuple / function type ラベルが型参照として出なくなりました** — `(x: Coordinate, y: Coordinate)` のような形ではラベルを無視し、実際の要素型だけを index するようにしました。
