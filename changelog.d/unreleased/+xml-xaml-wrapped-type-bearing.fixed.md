---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Wrapped XAML type-bearing attributes are now indexed as searchable class symbols** — continuing the follow-up work from PR #1329, the extractor now picks up `x:Class`, `x:DataType`, and `TargetType` values even when the attribute value is wrapped onto later lines, so `symbols` and `definition` can find the referenced types inside multiline XAML markup.

## 日本語

- **折り返しされた XAML の型関連属性を検索可能な class シンボルとして index するようになりました** — PR #1329 の follow-up として、`x:Class` / `x:DataType` / `TargetType` の値が後続行に折り返されていても拾えるようにし、`symbols` / `definition` が multiline XAML マークアップ内の参照型を見つけられるようになりました。
