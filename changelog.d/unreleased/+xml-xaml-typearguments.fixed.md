---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **XAML `x:TypeArguments` values are now indexed as searchable class symbols** — continuing the follow-up work from PR #1316, generic XAML declarations now surface each type argument so `symbols` and `definition` can find the referenced types instead of treating the generic markup as an opaque string.

## 日本語

- **XAML の `x:TypeArguments` 値を検索可能な class シンボルとして index するようになりました** — PR #1316 の follow-up として、generic な XAML 宣言で使われる各 type argument を拾うため、`symbols` / `definition` で generic マークアップ全体を不透明な文字列として扱わず、参照先の型を直接見つけられるようになりました。
