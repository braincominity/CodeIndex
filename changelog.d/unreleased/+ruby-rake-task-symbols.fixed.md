---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Ruby Rake task definitions are now indexed as functions** — `cdidx` records `task :build` and `task test: :environment` names, improving entrypoint search in Ruby/Rake projects.

## 日本語

- **Ruby の Rake task 定義を関数として索引するようになりました** — `cdidx` は `task :build` や `task test: :environment` の名前を記録し、Ruby/Rake プロジェクトの入口検索を改善します。
