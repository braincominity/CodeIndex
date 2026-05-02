---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **XAML `x:Static` member references now surface their containing types as searchable class symbols** — following up on PR #1333, the extractor now indexes type names referenced by `x:Static` expressions such as `x:Static local:Keys.AccentBrush` and `x:Static Member={x:Type local:Keys}.PrimaryStyleKey`, so resource-key style markup can still lead search to the owning type.

## 日本語

- **XAML の `x:Static` メンバー参照から包含 type を検索可能な class シンボルとして拾うようになりました** — PR #1333 の follow-up として、`x:Static local:Keys.AccentBrush` や `x:Static Member={x:Type local:Keys}.PrimaryStyleKey` のような式から type 名を index し、リソースキー系のマークアップでも所有 type に検索で辿れるようになりました。
