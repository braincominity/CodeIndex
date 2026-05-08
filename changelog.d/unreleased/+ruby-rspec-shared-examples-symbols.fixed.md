---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Ruby.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Ruby RSpec shared examples now create function symbols** — `cdidx` indexes `shared_examples "auditable" do` blocks so reusable spec contracts are searchable.

## 日本語

- **Ruby RSpec のshared examplesがfunction symbolを作るようになりました** — `cdidx` は `shared_examples "auditable" do` ブロックを索引し、再利用されるspec契約を検索可能にします。
