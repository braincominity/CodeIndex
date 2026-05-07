---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Markup.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **XAML `TemplateBinding` properties are now indexed** — `{TemplateBinding Background}` and `{TemplateBinding Property=local:ButtonChrome.BorderBrush}` now emit searchable property symbols, improving ControlTemplate navigation in C#/XAML projects.

## 日本語

- **XAML の `TemplateBinding` プロパティもインデックスされるようになりました** — `{TemplateBinding Background}` や `{TemplateBinding Property=local:ButtonChrome.BorderBrush}` を検索可能な property シンボルとして出力し、C#/XAML プロジェクトの ControlTemplate ナビゲーションを改善します。
