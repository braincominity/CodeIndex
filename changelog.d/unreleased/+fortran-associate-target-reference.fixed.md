---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran `associate` targets now appear in reference search** — selectors such as `associate (alias => target)` index the target name.

## 日本語

- **Fortran の `associate` target が参照検索に出るようになりました** — `associate (alias => target)` のような selector で target 側の名前を索引します。
