---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran submodule parent dependencies now appear in reference search** — `submodule (parent:ancestor) child` declarations index the parent module and ancestor submodule names.

## 日本語

- **Fortran submodule の親依存が参照検索に出るようになりました** — `submodule (parent:ancestor) child` 宣言で、親 module と ancestor submodule 名を索引します。
