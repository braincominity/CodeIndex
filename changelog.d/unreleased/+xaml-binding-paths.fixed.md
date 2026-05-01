---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **XAML binding paths are now indexed as property symbols** — common binding expressions such as `{Binding Title}`, `{Binding Path=ViewModel}`, and `{x:Bind ViewModel.SaveCommand}` now surface their bound property names in search results, making it easier to navigate from markup to the viewmodel or command code behind it.

## 日本語

- **XAML の binding path が property シンボルとして索引化されるようになりました** — `{Binding Title}`、`{Binding Path=ViewModel}`、`{x:Bind ViewModel.SaveCommand}` のような一般的な binding 式から対応するプロパティ名を検索結果に出せるようになり、markup から viewmodel や command の実装へ辿りやすくなりました。
