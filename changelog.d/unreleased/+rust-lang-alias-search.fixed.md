---
category: fixed
affected:
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - src/CodeIndex/Database/DbReader.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Rust search now accepts `rs` as a language alias** — query commands normalize `rs`, `r-s`, and `r s` to `rust`, and the CLI help alias list now advertises the shorthand, so Rust searches work with the form many users type first.

## 日本語

- **Rust 検索で `rs` を言語別名として受け付けるようになりました** — クエリ系コマンドが `rs`、`r-s`、`r s` を `rust` に正規化し、CLI の別名一覧にも短縮形が載るため、Rust 検索を多くの利用者が最初に入力する表記で実行できます。
