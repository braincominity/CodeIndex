---
category: fixed
affected:
  - src/CodeIndex/Database/DbReader.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
  - README.md
---

## English

- **Rust graph queries now accept path-qualified spellings** — `references`, `callers`, `callees`, and `inspect` now normalize Rust `crate::foo::Bar`-style paths and trailing `!` macro names to the stored leaf symbol name, so the query text copied from source resolves the same way as `symbols`.

## 日本語

- **Rust の graph 系クエリで path-qualified な表記を受け付けるようになりました** — `references`、`callers`、`callees`、`inspect` は Rust の `crate::foo::Bar` 形式の path と末尾 `!` の macro 名を保存済みの leaf シンボル名へ正規化するため、ソースからそのまま貼った検索語でも `symbols` と同じように解決されます。
