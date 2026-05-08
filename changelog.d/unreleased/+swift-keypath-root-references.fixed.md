---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift key path roots are now indexed as type references** — explicit roots such as `\User.name` and `\Order.customer.name` now surface their model types without treating property path segments as types.

## 日本語

- **Swift key path の root を型参照として index するようにしました** — `\User.name` や `\Order.customer.name` の明示的な root 型を拾い、プロパティ経路の各セグメントは型として扱わないようにしました。
