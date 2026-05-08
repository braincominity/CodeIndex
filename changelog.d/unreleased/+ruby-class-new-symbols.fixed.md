---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Ruby.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Ruby `Class.new` block assignments now create class symbols** — `cdidx` indexes declarations such as `User = Class.new(...) do` as classes so methods defined inside the block are searchable under the dynamic class.

## 日本語

- **Ruby の `Class.new` ブロック代入がclass symbolを作るようになりました** — `cdidx` は `User = Class.new(...) do` のような宣言をclassとして索引し、ブロック内メソッドを動的クラス配下で検索できるようにします。
