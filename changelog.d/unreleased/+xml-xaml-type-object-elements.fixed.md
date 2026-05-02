---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **XAML `x:Type` object-element syntax is now indexed as searchable class symbols** — following up on PR #1331, the extractor now recognizes `<x:Type ... TypeName="..."/>` and related `x:TypeExtension` forms, so object-element markup can surface referenced types even when the type name is wrapped across lines.

## 日本語

- **XAML の `x:Type` object-element 構文を検索可能な class シンボルとして index するようになりました** — PR #1331 の follow-up として、`<x:Type ... TypeName="..."/>` および関連する `x:TypeExtension` 形を認識し、TypeName が複数行に折り返されていても object-element マークアップ内の参照型を拾えるようになりました。
