---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift macro generic arguments are now indexed as type references** — calls such as `#Predicate<User>` and `#Expression<Order, Score>` expose their generic model types without turning non-generic compiler checks into type references.

## 日本語

- **Swift macro の generic 引数を型参照として index するようにしました** — `#Predicate<User>` や `#Expression<Order, Score>` のモデル型を見つけられるようにしつつ、generic ではないコンパイラチェックは型参照にしません。
