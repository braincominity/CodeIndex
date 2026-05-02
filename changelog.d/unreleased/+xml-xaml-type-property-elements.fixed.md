---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **XAML `x:Type.TypeName` property-element syntax is now indexed as searchable class symbols** — following up on PR #1332, the extractor now recognizes property-element forms such as `<x:Type.TypeName>` and `<x:TypeExtension.TypeName>`, so type names can still be searched when markup uses property-element syntax instead of attributes or object elements.

## 日本語

- **XAML の `x:Type.TypeName` property-element 構文を検索可能な class シンボルとして index するようになりました** — PR #1332 の follow-up として、`<x:Type.TypeName>` や `<x:TypeExtension.TypeName>` のような property-element 形を認識し、属性や object-element ではなく property-element 構文を使っていても参照型を検索できるようになりました。
