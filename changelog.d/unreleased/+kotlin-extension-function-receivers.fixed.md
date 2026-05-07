---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/KotlinReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin extension function receivers are now indexed as type references** — declarations such as `fun User.render()` and `fun Box<User>.unwrap()` now emit receiver type dependencies so `references User` can find extension functions that target that type.

## 日本語

- **Kotlin の extension function receiver を型参照として索引化するようになりました** — `fun User.render()` や `fun Box<User>.unwrap()` のような宣言で receiver 型の依存を発行し、`references User` から対象型への拡張関数を見つけられます。
