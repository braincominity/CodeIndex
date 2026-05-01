---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **VB `Enum` bodies now stay open for later declarations, and enum members are indexed** — `Enum` now participates in VB container range detection so a nested enum no longer cuts off later class or module members, and top-level enum members like `Red` / `Green` now surface as searchable `enum` symbols.

## 日本語

- **VB の `Enum` 本体が後続宣言を途中で切らず、enum メンバーも索引されるようになりました** — VB の container 範囲判定に `Enum` を含めたため、ネストした enum で外側クラスやモジュールの後続メンバーが切り捨てられなくなり、`Red` / `Green` のような enum メンバーも検索可能な `enum` シンボルとして出力されます。
