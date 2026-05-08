---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift associatedtype constraints and defaults now index type references** — protocol declarations such as `associatedtype Item: Identifiable` and `associatedtype Cache = MemoryCache<Item>` now expose those dependencies to reference queries.

## 日本語

- **Swift の associatedtype 制約とデフォルト型が型参照として index されるようになりました** — `associatedtype Item: Identifiable` や `associatedtype Cache = MemoryCache<Item>` のような protocol 宣言で、それらの依存型を参照クエリから見つけられるようにしました。
