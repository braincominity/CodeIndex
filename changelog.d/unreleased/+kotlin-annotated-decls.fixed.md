---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Kotlin annotation-bearing declarations are indexed now** — `SymbolExtractor` now skips leading Java/Kotlin-style annotations and Kotlin use-site targets before matching declarations, so `@Serializable data class`, `@field:Deprecated val`, `@get:JvmName val`, `@Deprecated fun`, and annotated secondary constructors stay searchable instead of being missed.

## 日本語

- **Kotlin の annotation 付き宣言が index されるようになりました** — `SymbolExtractor` が Java/Kotlin 風の先頭 annotation と Kotlin の use-site target を宣言マッチ前に読み飛ばすため、`@Serializable data class`、`@field:Deprecated val`、`@get:JvmName val`、`@Deprecated fun`、annotation 付き secondary constructor が検索対象から漏れなくなりました。
