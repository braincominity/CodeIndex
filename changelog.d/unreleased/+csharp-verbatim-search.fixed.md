---
category: fixed
affected:
  - src/CodeIndex/Database/DbContext.cs
  - src/CodeIndex/Database/DbSearchReader.cs
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - src/CodeIndex/Cli/SearchSnippetFormatter.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **C# exact substring search now normalizes verbatim qualified names** — `search --exact-substring` now treats C# verbatim prefixes as syntax only, so `Foo.Bar` can match `using @Foo.@Bar;` and other equivalent source spellings while leaving non-C# files unchanged.

## 日本語

- **C# の exact substring 検索で verbatim 修飾名を正規化するようになりました** — `search --exact-substring` は C# の verbatim 接頭辞を構文上の差分として扱うため、`Foo.Bar` で `using @Foo.@Bar;` など同値な表記にマッチでき、C# 以外のファイルはそのまま維持されます。
