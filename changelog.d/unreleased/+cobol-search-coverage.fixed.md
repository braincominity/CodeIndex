---
category: fixed
affected:
  - src/CodeIndex/Indexer/FileIndexer.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
---

## English

- **COBOL source files are now indexed and shown in `cdidx languages`** — `FileIndexer` now recognizes common COBOL extensions (`.cbl`, `.cob`, `.cobol`, `.cpy`), so COBOL projects are no longer skipped during indexing and their contents become searchable through the normal full-text path.

## 日本語

- **COBOL ソースファイルが index 対象になり、`cdidx languages` にも表示されるようになりました** — `FileIndexer` が一般的な COBOL 拡張子（`.cbl`、`.cob`、`.cobol`、`.cpy`）を認識するため、COBOL プロジェクトが indexing で落とされず、通常の全文検索経路で検索できるようになります。
