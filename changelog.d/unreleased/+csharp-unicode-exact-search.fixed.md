---
category: fixed
affected:
  - src/CodeIndex/Database/DbSearchReader.cs
  - src/CodeIndex/Database/ExactSourceSearchNormalizer.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **C# exact search now canonicalizes Unicode-escaped identifiers** — C# exact substring search now decodes `\uXXXX` and `\UXXXXXXXX` source escapes before applying existing `@identifier` and `global::` normalization, matching the escaped-identifier behavior already shared with Java and Kotlin.

## 日本語

- **C# exact 検索が Unicode escape 付き識別子を正規化するようになりました** — C# の exact substring 検索は既存の `@identifier` / `global::` 正規化の前に `\uXXXX` と `\UXXXXXXXX` の source escape をデコードし、Java / Kotlin と共有している escaped identifier の検索挙動に揃えるようになりました。
