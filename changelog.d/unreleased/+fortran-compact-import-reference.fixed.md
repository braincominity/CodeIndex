---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Compact Fortran `import` statements now appear in reference search** — `import::Name` and `import, only:Name` are indexed like spaced interface import statements.

## 日本語

- **compact な Fortran `import` 文が参照検索に出るようになりました** — `import::Name` と `import, only:Name` を空白ありの interface import と同じように索引します。
