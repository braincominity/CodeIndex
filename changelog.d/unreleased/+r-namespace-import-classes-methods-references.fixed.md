---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R NAMESPACE class and method imports now surface in reference search** — `importClassesFrom()` and `importMethodsFrom()` directives are indexed like `importFrom()` with package-qualified and leaf references.

## 日本語

- **R NAMESPACE の class / method import が参照検索に出るようになりました** — `importClassesFrom()` と `importMethodsFrom()` を `importFrom()` と同様に package-qualified / leaf reference として索引します。
