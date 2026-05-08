---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift typed throws now contribute type references** — `throws(ErrorType)` clauses are indexed as `type_reference` edges so `references` and impact-style queries can find dependencies on thrown error types.

## 日本語

- **Swift の typed throws が型参照として記録されるようになりました** — `throws(ErrorType)` 句を `type_reference` edge として index し、`references` や impact 系のクエリで thrown error type への依存を見つけられるようにしました。
