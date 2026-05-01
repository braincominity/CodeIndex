---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C and C++ `#include` targets are now indexed without delimiter noise** — include symbols now store `stdio.h`, `"project/foo.h"`, or bare macro targets instead of the raw directive text, which makes header searches more precise in C-family projects.

## 日本語

- **C / C++ の `#include` 対象を区切り記号なしでインデックスするようになりました** — include シンボルは生のディレクティブ文字列ではなく `stdio.h`、`"project/foo.h"`、あるいは裸のマクロ対象を保存するため、C 系プロジェクトでヘッダー検索をより正確にできます。
