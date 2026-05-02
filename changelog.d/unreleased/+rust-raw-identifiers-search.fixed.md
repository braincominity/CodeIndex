---
category: fixed
affected:
  - src/CodeIndex/Database/DbSymbolReader.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **Rust search now accepts raw identifiers without the `r#` prefix** — symbol and reference queries for names like `r#type` now resolve to the canonical stored form, so search entrypoints match Rust source spellings that use raw identifiers.

## 日本語

- **Rust 検索が `r#` プレフィックスなしの raw identifier を受け付けるようになりました** — `r#type` のような名前の symbol / reference query が、DB に保存されている正規化済みの形式へ解決されるため、raw identifier を使う Rust ソース表記でも検索入口が一致するようになります。
