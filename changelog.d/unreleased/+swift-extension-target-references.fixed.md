---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift extension targets now index as type references** — `extension Repository where ...` and `extension CacheStore: Protocol` declarations now expose the extended type to `references` queries.

## 日本語

- **Swift の extension 対象型が型参照として index されるようになりました** — `extension Repository where ...` や `extension CacheStore: Protocol` の宣言で、拡張対象の型を `references` クエリから見つけられるようにしました。
