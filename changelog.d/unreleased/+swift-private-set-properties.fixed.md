---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Swift `private(set)` and `fileprivate(set)` properties are now searchable** - Swift property extraction now accepts setter-restricted stored properties, so common access-controlled state like `private(set) var value` stays visible to `symbols` and related search workflows.

## 日本語

- **Swift の `private(set)` / `fileprivate(set)` 付きプロパティを検索できるようにしました** - Swift の property extraction で setter 制限付きの stored property を受け付けるようにしたため、`private(set) var value` のようなアクセス制御された状態も `symbols` などから見えるようになります。
