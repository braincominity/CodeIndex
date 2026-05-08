---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Ruby.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Ruby `Struct.new` block assignments now create class symbols** — `cdidx` indexes declarations such as `Result = Struct.new(...) do` as classes and keeps methods defined inside the block under that dynamic container.

## 日本語

- **Ruby の `Struct.new` ブロック代入がclass symbolを作るようになりました** — `cdidx` は `Result = Struct.new(...) do` のような宣言をclassとして索引し、ブロック内メソッドを動的container配下に保ちます。
