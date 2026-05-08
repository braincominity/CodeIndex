---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Ruby.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Ruby FactoryBot factories now create searchable symbols** — `cdidx` indexes `factory :user do` definitions as function symbols with Ruby block ranges.

## 日本語

- **Ruby FactoryBot のfactory定義が検索可能なsymbolを作るようになりました** — `cdidx` は `factory :user do` 定義をRubyブロック範囲付きのfunction symbolとして索引します。
