---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Swift declarations with same-line attributes now stay searchable** — the Swift symbol extractor now accepts common attributes such as `@available(...)` and `@discardableResult` before declaration keywords, so annotated `func`, `struct`, `typealias`, `extension`, and related symbols are indexed correctly.

## 日本語

- **Swift の同一行属性付き宣言も検索できるようになりました** — Swift のシンボル抽出で `@available(...)` や `@discardableResult` などの一般的な属性を宣言キーワードの前に受け入れるようにし、注釈付きの `func` / `struct` / `typealias` / `extension` などが正しくインデックスされるようになりました。
