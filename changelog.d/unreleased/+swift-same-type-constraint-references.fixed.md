---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Support/TrailingLambdaReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift same-type constraints now index right-hand type references** — `where Entity == User` and `where T.Output == Response` constraints now expose their concrete target types to `references` queries without adding phantom trailing-closure call edges.

## 日本語

- **Swift の same-type 制約右辺が型参照として index されるようになりました** — `where Entity == User` や `where T.Output == Response` の制約で、phantom な trailing-closure call edge を追加せずに具体的な対象型を `references` クエリから見つけられるようにしました。
