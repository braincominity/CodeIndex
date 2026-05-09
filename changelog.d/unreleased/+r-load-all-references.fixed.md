---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R `load_all()` development loaders now surface in reference search** — `devtools::load_all()` and `pkgload::load_all()` path arguments are indexed as references.

## 日本語

- **R の `load_all()` 開発用 loader が参照検索に出るようになりました** — `devtools::load_all()` / `pkgload::load_all()` の path 引数を reference として索引します。
