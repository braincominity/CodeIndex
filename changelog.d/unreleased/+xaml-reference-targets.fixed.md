---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Markup.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **XAML `x:Reference` targets are now indexed** — `{x:Reference RootPanel}`, `{x:Reference Name=NamedTarget}`, `<x:Reference Name="ObjectTarget" />`, and `<x:Reference.Name>PropertyTarget</x:Reference.Name>` now emit searchable property symbols, improving navigation between XAML references and named elements.

## 日本語

- **XAML の `x:Reference` 参照先もインデックスされるようになりました** — `{x:Reference RootPanel}`、`{x:Reference Name=NamedTarget}`、`<x:Reference Name="ObjectTarget" />`、`<x:Reference.Name>PropertyTarget</x:Reference.Name>` を検索可能な property シンボルとして出力し、XAML の参照と名前付き要素の間をたどりやすくしました。
