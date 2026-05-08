---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift collection shorthand constructors now index contained types** — `[User]()` and `[String: Handler]()` expose `User` and `Handler` as type references while subscript calls such as `items[index]()` stay ignored.

## 日本語

- **Swift の collection shorthand constructor 内の型を index するようにしました** — `[User]()` や `[String: Handler]()` から `User` / `Handler` を型参照として拾い、`items[index]()` のような subscript 呼び出しは除外します。
