---
category: fixed
issues:
  - 279
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Elixir symbol bodies now close at the matching `end` instead of the first nested one (#279)** — `SymbolExtractor` now tracks Elixir `do ... end` / `fn ... end` nesting separately from Ruby, so functions that contain nested `fn`, `case`, `if`, or `with` blocks keep the full body range. Added regressions for nested blocks, `, do:` shorthand, and downstream reference container attribution.

## 日本語

- **Elixir のシンボル本体が、最初に出てきた `end` ではなく対応する `end` で閉じるようになりました (#279)** — `SymbolExtractor` が Elixir の `do ... end` / `fn ... end` のネストを Ruby とは分けて追跡するようになり、`fn`、`case`、`if`、`with` を含む関数でも本来の body range を保てます。ネストした block、`, do:` の短縮形、そして downstream の reference container attribution の回帰テストを追加しています。
