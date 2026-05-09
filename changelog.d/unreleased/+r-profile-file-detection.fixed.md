---
category: fixed
affected:
  - src/CodeIndex/Indexer/Scanning/FileIndexer.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
---

## English

- **R startup profile files are now detected as R** — `.Rprofile` and `Rprofile.site` are indexed in the R language bucket so startup hooks and helper definitions are available to scoped search.

## 日本語

- **R の起動プロファイルファイルを R として検出するようになりました** — `.Rprofile` と `Rprofile.site` を R 言語として index するため、起動時 hook や helper 定義も scoped search の対象になります。
