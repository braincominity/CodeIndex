---
category: changed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Batch `goto :label` / `call :label` targets are now indexed as call references** — `batch` files now emit `call`-kind references for jump targets, so `references`, `callers`, `callees`, and `impact` can trace `goto :Build` / `call :Build` edges instead of treating batch labels as isolated symbols.

## 日本語

- **Batch の `goto :label` / `call :label` ターゲットを call 参照として索引するようになりました** — `batch` ファイルでもジャンプ先に `call` 種別の参照が出るため、`references` / `callers` / `callees` / `impact` で `goto :Build` / `call :Build` の関係を辿れるようになり、ラベルが孤立したシンボルのままになりません。
