---
category: added
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
  - README.md
---

## English

- **Markdown now indexes Setext headings and local anchor references** — `SymbolExtractor` recognizes both ATX and Setext headings as `heading` symbols, and local `#anchor` link targets are surfaced as searchable `reference` symbols.

## 日本語

- **Markdown で Setext 見出しと local anchor 参照を索引するようになりました** — `SymbolExtractor` は ATX と Setext の両方を `heading` シンボルとして認識し、`#anchor` の local 参照も検索可能な `reference` シンボルとして表面化します。
