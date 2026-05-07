---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Avalonia binding paths are now indexed from XAML** — `{CompiledBinding ...}` and `{ReflectionBinding ...}` now emit searchable property symbols, matching the existing `{Binding ...}` path extraction for Avalonia XAML projects.

## 日本語

- **Avalonia の Binding path も XAML からインデックスされるようになりました** — `{CompiledBinding ...}` と `{ReflectionBinding ...}` を検索可能な property シンボルとして出力し、Avalonia XAML プロジェクトでも既存の `{Binding ...}` と同じように path を検索できるようにしました。
