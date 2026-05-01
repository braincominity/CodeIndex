---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Kotlin `fun interface` declarations are now indexed for symbol search** — `fun interface` is treated as an interface symbol, so searches for Kotlin SAM types no longer miss these declarations.

## 日本語

- **Kotlin の `fun interface` 宣言がシンボル検索で index されるようになりました** — `fun interface` を interface シンボルとして扱うため、Kotlin の SAM 型を検索したときにこの宣言を取りこぼさなくなりました。
