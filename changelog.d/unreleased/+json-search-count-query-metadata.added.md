---
category: added
affected:
  - src/CodeIndex/Cli/JsonOutputContracts.cs
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **JSON `search --count` output now includes the original query on non-zero results** — count-based JSON search payloads now carry `query` alongside `count` and `files`, so machine consumers can correlate the summary with the exact search input.

## 日本語

- **JSON 形式の `search --count` 出力が非0件でも元の検索文字列を含むようになりました** — count ベースの JSON search payload に `count` と `files` に加えて `query` も載せることで、要約結果と検索入力を機械処理側で正確に対応付けられるようにしました。
