---
category: fixed
affected:
  - src/CodeIndex/Indexer/Scanning/FileIndexer.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
---
## English
- Fixed Visual Basic file detection so `.bas` modules are indexed as `vb` instead of being skipped.

## 日本語
- Visual Basic のファイル検出で、`.bas` モジュールをスキップせず `vb` として索引するよう修正しました。
