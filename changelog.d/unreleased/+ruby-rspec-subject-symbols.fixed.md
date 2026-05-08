---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Ruby.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Ruby RSpec named subjects now create property symbols** — `cdidx` indexes `subject(:profile) do` helper definitions with Ruby block ranges.

## 日本語

- **Ruby RSpec の名前付きsubjectがproperty symbolを作るようになりました** — `cdidx` は `subject(:profile) do` のhelper定義をRubyブロック範囲付きで索引します。
