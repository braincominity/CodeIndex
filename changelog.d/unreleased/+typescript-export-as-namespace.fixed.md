---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **TypeScript `export as namespace` declarations are now searchable as namespaces** — `SymbolExtractor` now recognizes UMD-style `export as namespace Foo;` declarations, so legacy declaration files expose their namespace anchors to `search` and other symbol-driven flows.

## 日本語

- **TypeScript の `export as namespace` 宣言を namespace として検索できるようになりました** — `SymbolExtractor` が UMD 形式の `export as namespace Foo;` 宣言を認識するため、古い declaration file でも namespace の検索アンカーが `search` などの symbol ベースの処理に現れます。
