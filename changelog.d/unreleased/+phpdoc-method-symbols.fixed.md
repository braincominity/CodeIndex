---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Php.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHPDoc `@method` declarations now emit function symbols** — dynamic methods documented on PHP classes are now searchable as function symbols, including optional return type metadata.

## 日本語

- **PHPDoc `@method` 宣言を function シンボルとして索引するようになりました** — PHP クラスに docblock で記録された動的メソッドが、任意の戻り値型 metadata とともに function symbol として検索可能になります。
