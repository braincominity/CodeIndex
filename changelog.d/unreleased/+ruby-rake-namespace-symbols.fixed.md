---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Ruby.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Ruby Rake namespaces now create namespace symbols** — `cdidx` indexes `namespace :db do` blocks and attaches nested tasks to the namespace container.

## 日本語

- **Ruby Rake の namespace がnamespace symbolを作るようになりました** — `cdidx` は `namespace :db do` ブロックを索引し、内側のtaskをnamespace container配下に置きます。
