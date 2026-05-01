---
category: changed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **XAML `x:Key` values are now indexed for search** — `.xaml` and `.axaml` files already treated as XAML now emit `property` symbols for resource keys such as `x:Key="AccentBrush"`, making ResourceDictionary names searchable alongside `x:Class` and `x:Name`.

## 日本語

- **XAML の `x:Key` 値も検索対象としてインデックスするようになりました** — XAML として扱われる `.xaml` / `.axaml` ファイルで、`x:Key="AccentBrush"` のようなリソースキーを `property` シンボルとして出力するようにし、`x:Class` / `x:Name` と並んで ResourceDictionary 名も検索できるようにしました。
