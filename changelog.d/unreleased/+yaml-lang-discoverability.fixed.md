---
category: fixed
affected:
  - src/CodeIndex/Cli/ConsoleUi.cs
  - src/CodeIndex/Cli/JsonOutputContracts.cs
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - src/CodeIndex/Mcp/McpToolHandlers.cs
  - tests/CodeIndex.Tests/ConsoleUiTests.cs
  - tests/CodeIndex.Tests/McpServerTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **YML now appears in language discovery output** — `cdidx languages`, MCP `languages`, and shell completion now surface `yml` alongside `yaml`, so discoverability matches the query alias that `--lang yml` already accepts.

## 日本語

- **YML が言語探索結果に表示されるようになりました** — `cdidx languages`、MCP の `languages`、および shell completion が `yaml` と並んで `yml` を出すため、`--lang yml` で受け付ける別名と discoverability が一致します。
