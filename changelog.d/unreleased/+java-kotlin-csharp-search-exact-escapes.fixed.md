---
category: fixed
affected:
  - src/CodeIndex/Database/ExactSourceSearchNormalizer.cs
  - src/CodeIndex/Database/DbContext.cs
  - src/CodeIndex/Database/DbReader.cs
  - src/CodeIndex/Database/DbSearchReader.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
  - README.md
---

## English

- **Java, Kotlin, and C# exact search now canonicalize escaped source identifiers** — exact `search` and `find` now treat Java Unicode escapes, Kotlin backticked identifiers, and C# verbatim / `global::` spellings as the same source identifier so canonical and source forms match consistently.

## 日本語

- **Java / Kotlin / C# の exact 検索で escaped source identifier を正規化するようになりました** — exact `search` / `find` では Java の Unicode escape、Kotlin の backticked identifier、C# の verbatim / `global::` 表記を同一視し、canonical と source の表記を一貫して検索できるようにしました。
