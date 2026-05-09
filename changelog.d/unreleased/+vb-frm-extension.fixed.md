---
category: fixed
affected:
  - src/CodeIndex/Indexer/Scanning/FileIndexer.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
---
## English
- Fixed Visual Basic file detection so classic `.frm` form files are indexed as `vb`.

## 日本語
- Visual Basic のファイル検出で、classic VB の `.frm` フォームファイルを `vb` として索引するよう修正しました。
