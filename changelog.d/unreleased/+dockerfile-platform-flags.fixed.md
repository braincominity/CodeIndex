---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Dockerfile stage search now handles `FROM --platform=...` lines** — `cdidx` now recognizes named stages and base images when a Dockerfile `FROM` instruction includes a platform flag, so searches and dependency references stay accurate for modern multi-stage builds.

## 日本語

- **Dockerfile の stage 検索が `FROM --platform=...` 行に対応しました** — Dockerfile の `FROM` 命令に platform フラグが付いていても、`cdidx` が名前付き stage と base image を正しく認識するため、現代的な multi-stage build でも検索結果と依存参照の精度が保たれます。
