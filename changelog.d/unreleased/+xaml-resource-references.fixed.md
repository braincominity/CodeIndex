---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Markup.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **XAML resource references are now indexed** — `{StaticResource PrimaryBrush}` and `{DynamicResource ResourceKey={x:Static local:Keys.AccentBrush}}` now emit searchable property symbols, improving navigation between resource declarations and resource consumers.

## 日本語

- **XAML のリソース参照もインデックスされるようになりました** — `{StaticResource PrimaryBrush}` や `{DynamicResource ResourceKey={x:Static local:Keys.AccentBrush}}` を検索可能な property シンボルとして出力し、リソース宣言と利用箇所の間をたどりやすくしました。
