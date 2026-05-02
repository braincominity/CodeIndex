---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# operator usages are now indexed as references** — symbolic operator forms such as `x ++ y`, `x >>= f`, and `(++)` now emit searchable `call`-style references, so `references` and `callers` can surface operator call sites alongside ordinary function calls.

## 日本語

- **F# の operator 使用箇所が reference としてインデックスされるようになりました** — `x ++ y` / `x >>= f` / `(++)` のような symbolic operator 形も検索可能な `call` 風 reference を出すため、`references` / `callers` で通常の関数呼び出しと同様に operator の呼び出し箇所を辿れるようになりました。
