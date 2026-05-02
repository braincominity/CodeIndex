---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **XAML `x:Type` markup extensions now surface their referenced types as searchable class symbols** — the extractor now scans generic `{x:Type ...}` and `{x:TypeExtension ...}` markup extensions in addition to the existing type-bearing attributes and property/object elements, so type references embedded in ordinary attribute values are discoverable by `symbols` and `RunSymbols`.

## 日本語

- **XAML の `x:Type` markup extension から参照 type を検索可能な class シンボルとして拾うようになりました** — 既存の type-bearing 属性や property/object element に加えて、一般的な `{x:Type ...}` / `{x:TypeExtension ...}` markup extension も走査するようにし、通常の属性値に埋め込まれた type 参照も `symbols` と `RunSymbols` から辿れるようにしました。
