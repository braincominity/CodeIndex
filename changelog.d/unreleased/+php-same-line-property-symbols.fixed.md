---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Php.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHP same-line property lists keep every declared member searchable** — declarations such as `public string $first, $last;` now index the later properties too, while ignoring commas inside defaults and string literals.

## 日本語

- **PHP の同一行プロパティリストで全メンバーを検索可能にしました** — `public string $first, $last;` のような宣言で後続プロパティも索引し、初期値や文字列リテラル内のカンマは無視します。
