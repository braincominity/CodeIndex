---
category: added
affected:
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - src/CodeIndex/Cli/SearchSnippetFormatter.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **JSON search output now includes the original query string** — each compact `search` result row now carries `query`, and zero-result JSON payloads for `search` also include `query` so machine consumers can correlate results with the exact search input.

## 日本語

- **JSON 形式の search 出力に元の検索文字列が含まれるようになりました** — `search` の compact 結果行に `query` を追加し、`search` の 0 件 JSON payload にも `query` を含めることで、機械処理側で結果と検索入力を正確に対応付けやすくしました。
