---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran `use` rename lists without `only:` now appear in reference search** — `use module, local => remote` indexes both the local alias and remote symbol.

## 日本語

- **`only:` なしの Fortran `use` rename list が参照検索に出るようになりました** — `use module, local => remote` で local alias と remote symbol の両方を索引します。
