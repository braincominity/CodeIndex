---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **XAML `TargetType` values are now indexed as searchable class symbols** — `Style`, `ControlTemplate`, and similar XAML elements now surface their `TargetType` values alongside `x:DataType`, so `symbols` and `definition` can find controls declared through XML markup instead of only matching the raw text.

## 日本語

- **XAML の `TargetType` 値を検索可能な class シンボルとして index するようになりました** — `Style` や `ControlTemplate` などの XAML 要素で指定された `TargetType` を `x:DataType` と同様に拾うため、`symbols` / `definition` で XML マークアップ経由のコントロール定義を raw text だけに頼らず見つけられるようになりました。
