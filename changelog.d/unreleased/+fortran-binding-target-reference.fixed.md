---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran type-bound binding targets now appear in reference search** — `procedure :: binding => implementation` and `generic :: assignment(=) => assign_impl` declarations index their implementation targets.

## 日本語

- **Fortran の type-bound binding target が参照検索に出るようになりました** — `procedure :: binding => implementation` や `generic :: assignment(=) => assign_impl` の宣言で、実装側ターゲットを索引します。
