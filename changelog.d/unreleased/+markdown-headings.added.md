---
category: added
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - src/CodeIndex/Database/DbSymbolReader.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
  - tests/CodeIndex.Tests/McpServerTests.cs
  - README.md
---

## English

- **Markdown headings now appear as `heading` symbols in `symbols` and `outline`** — `SymbolExtractor` indexes ATX headings outside fenced code blocks, the outline depth logic can nest them, and the MCP `languages` tool now reports Markdown as symbol-aware.

## 日本語

- **Markdown の見出しが `symbols` と `outline` で `heading` シンボルとして見えるようになりました** — `SymbolExtractor` は fenced code block 外の ATX 見出しを索引し、outline の深さ計算でネストを表現できるようになり、MCP の `languages` ツールでも Markdown がシンボル対応言語として表示されます。
