---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL programs now participate in symbol and call-graph extraction** — `SymbolExtractor` now records `PROGRAM-ID.` declarations as COBOL symbols, and `ReferenceExtractor` captures `CALL` targets so COBOL files can surface in `symbols`, `callers`, `callees`, and `impact` instead of being search-only.

## 日本語

- **COBOL プログラムが symbol / call graph 抽出の対象になりました** — `SymbolExtractor` が `PROGRAM-ID.` 宣言を COBOL symbol として記録し、`ReferenceExtractor` が `CALL` 対象を拾うため、COBOL ファイルも search-only ではなく `symbols` / `callers` / `callees` / `impact` に現れるようになります。
