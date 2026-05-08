---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift typealias right-hand sides now index referenced types** — aliases such as `typealias Loader = (Request) -> Response` now expose the aliased input, output, and generic argument types to `references` queries.

## 日本語

- **Swift の typealias 右辺が参照型として index されるようになりました** — `typealias Loader = (Request) -> Response` のような alias で、入力型・出力型・generic 引数型を `references` クエリから見つけられるようにしました。
