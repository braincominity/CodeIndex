---
category: fixed
affected:
  - src/CodeIndex/Indexer/FileIndexer.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
---

## English

- **Go module and workspace manifests now index as Go files** — `go.mod` and `go.work` are recognized as `go`, so module and workspace manifests participate in Go search results instead of being left out as unknown filenames.

## 日本語

- **Go の module / workspace マニフェストが Go ファイルとしてインデックスされるようになりました** — `go.mod` と `go.work` を `go` として認識するため、モジュールおよびワークスペースのマニフェストも Go の検索結果に含まれ、未知のファイル名として取りこぼされなくなります。
