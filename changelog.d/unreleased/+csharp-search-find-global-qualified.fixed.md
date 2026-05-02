---
category: fixed
affected:
  - src/CodeIndex/Database/CSharpVerbatimNameNormalizer.cs
  - src/CodeIndex/Database/DbReader.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
  - README.md
---

## English

- **C# exact `search` / `find` now canonicalize `global::` only at namespace starts** — `global::Foo.Bar` matches `Foo.Bar` in exact mode, while mid-path text such as `Foo.global::Bar` is left untouched. `find` now follows the same rule as `search`.

## 日本語

- **C# の exact `search` / `find` で `global::` を namespace 開始位置に限って正規化するようになりました** — exact モードでは `global::Foo.Bar` を `Foo.Bar` と同一視しつつ、`Foo.global::Bar` のような途中の文字列はそのまま残します。`find` も `search` と同じルールに追従します。
