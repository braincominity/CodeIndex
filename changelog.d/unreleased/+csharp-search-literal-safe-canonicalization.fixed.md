---
category: fixed
affected:
  - src/CodeIndex/Database/DbSearchReader.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **C# literal-safe `search` now canonicalizes verbatim and `global::` spellings before FTS matching** — queries like `global::Foo.Bar` and `@Foo.@Bar` now search the same indexed C# text as `Foo.Bar`, so default search no longer misses canonical source spellings when the query uses C#-specific escapes.

## 日本語

- **C# の literal-safe `search` で verbatim / `global::` 表記を FTS 前に正規化するようになりました** — `global::Foo.Bar` や `@Foo.@Bar` のようなクエリでも、`Foo.Bar` と同じ C# インデックス済みテキストを検索するため、C# 固有の escape 表記が原因で canonical な source 表記を取りこぼさなくなります。
