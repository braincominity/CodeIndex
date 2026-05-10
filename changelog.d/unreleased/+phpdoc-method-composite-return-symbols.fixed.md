---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Php.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHPDoc `@method` symbols now accept composite return types** — nullable, union, intersection, and generic return type spellings no longer prevent dynamic method declarations from being indexed.

## 日本語

- **PHPDoc `@method` シンボルが複合戻り値型を受け付けるようになりました** — nullable / union / intersection / generic の戻り値型表記で、動的メソッド宣言の索引が落ちなくなります。
