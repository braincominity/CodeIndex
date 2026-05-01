---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - README.md
---

## English

- **COBOL paragraph graphing now works for `PERFORM` targets** — `SymbolExtractor` now records COBOL paragraph labels inside `PROCEDURE DIVISION`, and `ReferenceExtractor` resolves `PERFORM` targets as call edges so COBOL files can participate in paragraph-level `callers` / `callees` instead of only exposing program-level calls.

## 日本語

- **COBOL の paragraph graph が `PERFORM` 対象でも動作するようになりました** — `SymbolExtractor` が `PROCEDURE DIVISION` 内の COBOL paragraph label を記録し、`ReferenceExtractor` が `PERFORM` 対象を call edge として解決するため、COBOL ファイルでも program-level だけでなく paragraph-level の `callers` / `callees` が使えるようになります。
