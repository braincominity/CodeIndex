---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift closure literal signatures now index type references** — inline closures such as `{ (value: Input) -> Output in ... }` now expose their parameter and return types to `references` queries.

## 日本語

- **Swift の closure literal signature が型参照として index されるようになりました** — `{ (value: Input) -> Output in ... }` のような inline closure で、parameter 型と戻り値型を `references` クエリから見つけられるようにしました。
