---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# match arms now contribute search hits for calls after `->`** — `ReferenceExtractor` now treats `match ... -> printfn ...` style branches as F# space-application contexts, so common calls inside pattern matches stay searchable instead of being missed.

## 日本語

- **F# の match arm で `->` の後ろに続く呼び出しも検索対象になるようになりました** — `ReferenceExtractor` が `match ... -> printfn ...` のような F# の branch を空白区切り application として扱うため、pattern match 内のよく使う呼び出しを取りこぼしにくくなりました。
