---
category: fixed
affected:
  - src/CodeIndex/Database/DbReader.cs
  - src/CodeIndex/Database/DbSymbolReader.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **Rust exact reference search now keeps qualified raw macro spellings path-aware** — queries such as `crate::r#type!` stay on the qualified `crate::type` form instead of collapsing to the bare leaf, so the follow-up candidate from PR #1342 is addressed without changing the broader bare `r#type!` behavior.

## 日本語

- **Rust の exact な reference search で qualified な raw macro 表記を path-aware のまま扱うようにしました** — `crate::r#type!` のような query は bare な leaf へ潰さず `crate::type` のまま扱うため、PR #1342 の follow-up candidate を解消しつつ、bare な `r#type!` の挙動はそのまま維持しています。
