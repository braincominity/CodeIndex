---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R package loads now index quoted names and `requireNamespace()` imports** — `library("pkg")`, `require("pkg")`, and `requireNamespace("pkg")` now produce import symbols, while `requireNamespace` is suppressed from call-style reference noise so R package usage stays searchable without extra chatter.

## 日本語

- **R の package 読み込みで引用付き名前と `requireNamespace()` を import として索引するようになりました** — `library("pkg")` / `require("pkg")` / `requireNamespace("pkg")` が import symbol を生成し、`requireNamespace` は call 由来のノイズからも除外されるため、R の package 利用を余計な雑音なしで検索しやすくなります。
