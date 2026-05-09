---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran intrinsic kind parameters now appear in reference search** — declarations such as `integer(c_int)` and `real(kind=real64)` index their named kind parameters as type references.

## 日本語

- **Fortran の intrinsic 型 kind パラメータが参照検索に出るようになりました** — `integer(c_int)` や `real(kind=real64)` のような宣言で、名前付き kind パラメータを型参照として索引します。
