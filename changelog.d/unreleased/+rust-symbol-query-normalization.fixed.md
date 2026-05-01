---
category: fixed
affected:
  - src/CodeIndex/Database/DbSymbolReader.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
  - README.md
---

## English

- **Rust symbol searches now accept path-qualified spellings** — `symbols` queries in Rust normalize `crate::foo::Bar`-style paths, and still strip a trailing `!` from macro names before lookup, so users can paste the spelling they see in source and still hit the stored leaf symbol.

## 日本語

- **Rust の symbol 検索で path-qualified な表記を受け付けるようになりました** — Rust の `symbols` クエリは `crate::foo::Bar` 形式の path を正規化し、macro 名の末尾 `!` も検索前に除去するため、ソースに見える表記をそのまま貼っても保存済みの leaf シンボルに到達できます。
