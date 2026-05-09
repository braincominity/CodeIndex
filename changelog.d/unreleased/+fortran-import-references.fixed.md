---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran interface imports now appear in reference search** — `import :: Name` and `import, only: Name` statements now index imported names as type references for dependency lookups.

## 日本語

- **Fortran interface の import が参照検索に出るようになりました** — `import :: Name` と `import, only: Name` の import 名を型参照として索引し、依存検索で辿れるようにしました。
