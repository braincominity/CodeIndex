---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **XAML `x:DataType` values are now indexed as class symbols** — markup that declares a binding context type can now surface the bound viewmodel or backing type in search results, making it easier to jump from XAML to the code that owns the data shape.

## 日本語

- **XAML の `x:DataType` が class シンボルとして索引化されるようになりました** — binding context の型を宣言した markup から、その viewmodel や backing type を検索結果に出せるようになり、XAML からデータ形状を持つコードへ辿りやすくなりました。
