---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Markup.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **XAML `ElementName` binding references are now indexed** — `{Binding ElementName=SearchBox, Path=Text}`, `<Binding ElementName="RootPanel" />`, and `<Binding.ElementName>DetailsList</Binding.ElementName>` now emit searchable property symbols, improving navigation from bindings back to named XAML elements.

## 日本語

- **XAML の `ElementName` binding 参照もインデックスされるようになりました** — `{Binding ElementName=SearchBox, Path=Text}`、`<Binding ElementName="RootPanel" />`、`<Binding.ElementName>DetailsList</Binding.ElementName>` を検索可能な property シンボルとして出力し、binding から名前付き XAML 要素へたどりやすくしました。
