---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **XAML `x:TypeArguments` now expands nested generic constructor shapes** — continuing the follow-up work from PR #1320, the extractor now recursively peels generic type arguments like `Outer(Inner(A, B), C)` so `symbols` and `definition` can find the referenced types inside more complex XAML markup.

## 日本語

- **XAML の `x:TypeArguments` が入れ子の generic constructor 形状を展開するようになりました** — PR #1320 の follow-up として、`Outer(Inner(A, B), C)` のような generic 型引数を再帰的に展開し、`symbols` / `definition` がより複雑な XAML マークアップ内の参照型も見つけられるようになりました。
