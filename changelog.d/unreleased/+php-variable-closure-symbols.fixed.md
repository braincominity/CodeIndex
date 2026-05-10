---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHP variable-bound closures are now searchable** — assignments such as `$handler = function (...) {}` and `$mapper = fn (...) => ...` now emit function symbols named after the bound variable.

## 日本語

- **PHP の変数束縛 closure を検索できるようになりました** — `$handler = function (...) {}` や `$mapper = fn (...) => ...` のような代入を、束縛先の変数名を持つ function シンボルとして出します。
