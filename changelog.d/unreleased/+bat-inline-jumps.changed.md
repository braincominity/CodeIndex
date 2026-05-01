---
category: changed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Batch jump parsing now handles inline `if errorlevel` and chained commands** — batch reference extraction now recognizes `if errorlevel 1 goto :label` and chained forms such as `goto :A & call :B`, so multi-step control flow is visible to `references`, `callers`, `callees`, and `impact`.

## 日本語

- **Batch のジャンプ解析が inline の `if errorlevel` と連結コマンドに対応しました** — batch の参照抽出が `if errorlevel 1 goto :label` や `goto :A & call :B` のような形も認識するようになり、複数ステップの制御フローが `references` / `callers` / `callees` / `impact` から見えるようになりました。
