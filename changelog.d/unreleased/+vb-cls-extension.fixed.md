---
category: fixed
affected:
  - src/CodeIndex/Indexer/Scanning/FileIndexer.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
---
## English
- Fixed Visual Basic file detection so classic `.cls` class modules are indexed as `vb`.

## 日本語
- Visual Basic のファイル検出で、classic VB の `.cls` class module を `vb` として索引するよう修正しました。
