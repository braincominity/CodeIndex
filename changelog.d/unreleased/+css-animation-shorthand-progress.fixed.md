---
category: fixed
affected:
  - src/CodeIndex/Indexer/CssReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **CSS animation shorthand references now advance past timing tokens** - duration-first values such as `animation: 250ms ease-in fade-in` and keyword-only values such as `animation: none` no longer risk stalling reference extraction.

## 日本語

- **CSS animation shorthand reference が timing token を読み飛ばして進むようになりました** - `animation: 250ms ease-in fade-in` のような duration-first 値や `animation: none` のような keyword-only 値で reference 抽出が停止するリスクをなくしました。
