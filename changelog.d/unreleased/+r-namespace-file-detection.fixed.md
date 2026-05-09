---
category: fixed
affected:
  - src/CodeIndex/Indexer/Scanning/FileIndexer.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
---

## English

- **R package `NAMESPACE` files are now detected as R** — extensionless `NAMESPACE` files are indexed in the R language bucket so package export/import directives participate in scoped search.

## 日本語

- **R パッケージの `NAMESPACE` ファイルを R として検出するようになりました** — 拡張子のない `NAMESPACE` ファイルを R 言語として index するため、package の export/import ディレクティブも scoped search の対象になります。
