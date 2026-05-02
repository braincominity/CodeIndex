---
category: fixed
affected:
  - src/CodeIndex/Indexer/FileIndexer.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
  - README.md
---

## English

- **C++ `.hh` headers are now indexed as C++** — `FileIndexer` recognizes `.hh` as `cpp`, so C++ headers in that common naming style now participate in symbol and reference search instead of being left to the C fallback.

## 日本語

- **C++ の `.hh` ヘッダーを C++ として index するようになりました** — `FileIndexer` が `.hh` を `cpp` として認識するため、その一般的な命名規則の C++ ヘッダーも C のフォールバックに落ちず、symbol / reference search に参加します。
