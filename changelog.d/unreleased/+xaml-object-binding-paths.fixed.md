---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Markup.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **XAML object-element binding paths are now indexed** — `<Binding Path="ViewModel.FirstName" />` and `<Binding.Path>Profile.DisplayName</Binding.Path>` now emit searchable property symbols, improving navigation for MultiBinding and property-element XAML forms.

## 日本語

- **XAML の object-element binding path もインデックスされるようになりました** — `<Binding Path="ViewModel.FirstName" />` や `<Binding.Path>Profile.DisplayName</Binding.Path>` を検索可能な property シンボルとして出力し、MultiBinding や property-element 形式の XAML ナビゲーションを改善します。
