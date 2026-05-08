---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/CSharpSymbolNameNormalizer.cs
  - src/CodeIndex/Indexer/Symbols/JavaSymbolNameNormalizer.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **C# and Java Unicode-escaped declarations now index under canonical symbol names** - declarations whose identifiers use source-only Unicode escape syntax now appear in symbol and exact-name definition searches by their decoded names.

## 日本語

- **C# / Java の Unicode escape 付き宣言を canonical な symbol 名でインデックスするようになりました** - source-only な Unicode escape 構文を使った識別子の宣言を decode 済みの名前で保持し、symbol 検索や exact-name definition 検索で見つけられるようにしました。
