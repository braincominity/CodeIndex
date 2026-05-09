---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran binding target lists are fully indexed** — generic/type-bound declarations such as `generic :: assignment(=) => assign_a, assign_b` now index every implementation target.

## 日本語

- **Fortran の binding target list をすべて索引するようになりました** — `generic :: assignment(=) => assign_a, assign_b` のような generic/type-bound 宣言で、すべての実装ターゲットを索引します。
