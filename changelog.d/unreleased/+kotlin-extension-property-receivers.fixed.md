---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/KotlinReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin extension property receivers are now indexed as type references** — declarations such as `val User.displayName: String` and `var Box<User>.selected: User` now emit receiver type dependencies for `references` and `impact`.

## 日本語

- **Kotlin の extension property receiver を型参照として索引化するようになりました** — `val User.displayName: String` や `var Box<User>.selected: User` のような宣言で receiver 型の依存を発行し、`references` / `impact` で追跡できます。
