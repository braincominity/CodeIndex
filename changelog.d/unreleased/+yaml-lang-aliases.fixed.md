---
category: fixed
affected:
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - src/CodeIndex/Mcp/McpToolHandlers.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **`--lang yml` now resolves to `yaml`** — query commands accept the common YML shorthand as an alias, so `search`, `files`, `symbols`, and related filters no longer return zero rows when users pass the file-format name they expect.

## 日本語

- **`--lang yml` が `yaml` として解決されるようになりました** — クエリ系コマンドが YML の慣用表記を別名として受け付けるため、`search` / `files` / `symbols` などで、期待するファイル形式名をそのまま指定しても 0 件になりにくくなります。
