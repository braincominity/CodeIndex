---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **VB enum members now survive leading attributes and multi-line initializers** — the enum member fallback now skips attribute blocks and keeps scanning through continued initializer lines, so members like `Legacy`, `Complex`, and `Ready` remain searchable even when the declaration is split across lines.

## 日本語

- **VB の enum member が先頭属性や複数行 initializer を含んでも索引されるようになりました** — enum member の fallback が attribute block を飛ばし、継続する initializer 行を追跡するため、`Legacy` / `Complex` / `Ready` のような member も改行をまたいだ宣言で検索可能なままになります。
