---
category: added
affected:
  - src/CodeIndex/Indexer/References/Languages/JavaReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - USER_GUIDE.md
  - DEVELOPER_GUIDE.md
---

## English

- **Java sealed `permits` lists now emit graph references** — `sealed interface Shape permits Circle, Square` now records `Circle` and `Square` as `type_reference` edges so `references`, `deps`, and impact-style graph queries can see permitted subtype dependencies.

## 日本語

- **Java sealed 型の `permits` リストを graph 参照として記録するようになりました** — `sealed interface Shape permits Circle, Square` が `Circle` と `Square` を `type_reference` edge として記録するため、`references`、`deps`、impact 系の graph query で許可サブタイプ依存を見られます。
