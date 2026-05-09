---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran `select type` guards now appear in reference search** — `type is (Name)` and `class is (Name)` branches are indexed as type references so type-dispatch dependencies are discoverable.

## 日本語

- **Fortran の `select type` guard が参照検索に出るようになりました** — `type is (Name)` と `class is (Name)` の分岐を型参照として索引し、型分岐の依存を見つけられるようにしました。
