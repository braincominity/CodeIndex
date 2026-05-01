---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - README.md
---

## English

- **F# space-separated application is now indexed for graph queries** — `references`, `callers`, and `callees` now capture common F# non-paren application forms such as `printfn "x"` and `List.map increment numbers`, so the graph covers more idiomatic functional code instead of falling back to text search.

## 日本語

- **F# の空白区切り application も graph query で索引されるようになりました** — `references`、`callers`、`callees` が `printfn "x"` や `List.map increment numbers` のような F# の典型的な non-paren application を拾うようになり、より慣用的な関数型コードもテキスト検索に頼らず辿れるようになりました。
