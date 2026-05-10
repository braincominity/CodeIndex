---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Fortran `namelist` members now appear in symbol search** — names in `namelist /group/ a, b` declarations are indexed as properties.

## 日本語

- **Fortran の `namelist` member が symbol search に出るようになりました** — `namelist /group/ a, b` の名前を property として索引します。
