---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Fortran `common` block members now appear in symbol search** — member names in `common /block/ a, b` declarations are indexed as properties.

## 日本語

- **Fortran の `common` block member が symbol search に出るようになりました** — `common /block/ a, b` の member 名を property として索引します。
