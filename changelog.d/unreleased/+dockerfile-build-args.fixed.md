---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **Dockerfile `ARG` build arguments are now searchable as `property` symbols** - Dockerfile symbol extraction now indexes `ARG` declarations such as `NODE_VERSION` and `APP_HOME`, making build-time knobs visible to `symbols`, `search`, and related symbol-aware workflows.

## 日本語

- **Dockerfile の `ARG` ビルド引数を `property` シンボルとして検索できるようにしました** - Dockerfile の symbol extraction で `NODE_VERSION` や `APP_HOME` のような `ARG` 宣言をインデックスするため、ビルド時の設定値を `symbols` / `search` などのシンボル対応ワークフローから名前でたどれます。
