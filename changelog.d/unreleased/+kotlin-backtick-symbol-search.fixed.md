---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/KotlinSymbolNameNormalizer.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Kotlin backticked declarations now use canonical symbol names** - declarations whose names are Kotlin keywords or contain spaces / punctuation are indexed without the source-only backticks, so exact-name definition search can find them by their canonical names.

## 日本語

- **Kotlin の backtick 付き宣言を canonical な symbol 名でインデックスするようになりました** - Kotlin keyword や空白 / 記号を含む名前の宣言を source-only な backtick なしで保持し、exact-name の definition 検索で canonical 名から見つけられるようにしました。
