---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# active pattern definitions are now searchable by their case names** — `SymbolExtractor` now emits searchable symbols for `let (|Foo|Bar|)` definitions, so active pattern cases like `Foo`, `Bar`, and partial-pattern forms such as `ParseInt` show up in search results.

## 日本語

- **F# の active pattern 定義を case 名で検索できるようになりました** — `SymbolExtractor` が `let (|Foo|Bar|)` の定義から検索可能な symbol を出すようになり、`Foo` / `Bar` や `ParseInt` のような partial-pattern 形式も検索結果に出るようになります。
