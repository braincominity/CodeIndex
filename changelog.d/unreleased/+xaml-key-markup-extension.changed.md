---
category: changed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **XAML `x:Key` markup extensions are now normalized before indexing** — resource keys such as `x:Key="{x:Static local:Keys.AccentBrush}"` are indexed using the inner reference instead of the raw brace-wrapped markup extension, which makes `ResourceDictionary` keys easier to search.

## 日本語

- **XAML の `x:Key` markup extension を正規化してからインデックスするようになりました** — `x:Key="{x:Static local:Keys.AccentBrush}"` のようなリソースキーは、波括弧付きの markup extension そのものではなく内側の参照名でインデックスされるため、`ResourceDictionary` のキーをより検索しやすくなります。
