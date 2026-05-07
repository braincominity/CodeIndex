---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Markup.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **XAML wrapped search attributes are now indexed** — `x:Name`, `x:Key`, and common event handler attributes split across lines are now emitted as searchable symbols, so C#/XAML code-behind lookups still find names such as `SaveButton` and `OnSaveClicked`.

## 日本語

- **折り返された XAML 検索属性もインデックスされるようになりました** — 複数行に分割された `x:Name`、`x:Key`、一般的なイベントハンドラ属性を検索可能なシンボルとして出力し、`SaveButton` や `OnSaveClicked` のような C#/XAML コードビハインド名を引き続き見つけられるようにしました。
