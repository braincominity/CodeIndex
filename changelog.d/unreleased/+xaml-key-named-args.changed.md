---
category: changed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **XAML `x:Key` normalization now handles named arguments more explicitly** — resource keys such as `x:Key="{x:Static Member={x:Type local:Keys}.AccentBrush}"` now resolve through the nested markup extension instead of relying on a whitespace split, making `ResourceDictionary` keys with named arguments searchable in a more predictable way.

## 日本語

- **XAML の `x:Key` 正規化が named argument をより明示的に扱うようになりました** — `x:Key="{x:Static Member={x:Type local:Keys}.AccentBrush}"` のようなリソースキーは、空白区切りの heuristic に頼らず nested markup extension をたどって解決するため、named argument を含む `ResourceDictionary` のキーもより予測しやすく検索できます。
