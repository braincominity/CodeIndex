---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Ruby.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Ruby RSpec `let` helpers now create property symbols** — `cdidx` indexes `let(:user) do` and `let!(:account) do` helper definitions with Ruby block ranges.

## 日本語

- **Ruby RSpec の `let` helper がproperty symbolを作るようになりました** — `cdidx` は `let(:user) do` と `let!(:account) do` のhelper定義をRubyブロック範囲付きで索引します。
