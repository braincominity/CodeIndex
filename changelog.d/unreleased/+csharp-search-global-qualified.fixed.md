---
category: fixed
affected:
  - src/CodeIndex/Database/CSharpVerbatimNameNormalizer.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
  - README.md
---

## English

- **C# exact substring search now canonicalizes `global::` prefixes** — `search` in `--exact` / `--exact-substring` mode now treats `global::Foo.Bar` the same as `Foo.Bar`, matching the existing verbatim `@` normalization so C# queries find canonical and source spellings consistently.

## 日本語

- **C# の exact / exact-substring 検索で `global::` 接頭辞を正規化するようになりました** — `search` の `--exact` / `--exact-substring` モードで `global::Foo.Bar` を `Foo.Bar` と同一視し、既存の verbatim `@` 正規化と同じく C# の canonical 表記と source 表記を一貫して検索できるようにしました。
