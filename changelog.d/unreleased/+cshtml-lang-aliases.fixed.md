---
category: fixed
affected:
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - src/CodeIndex/Database/DbReader.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Razor pages now accept `cshtml` and `razor` as C# language filters** — `search --lang cshtml` and `search --lang razor` now normalize to `csharp`, so Razor/Blazor files stay searchable through the CLI and database query APIs.

## 日本語

- **Razor ページで `cshtml` と `razor` を C# の言語フィルタとして使えるようにしました** — `search --lang cshtml` と `search --lang razor` が `csharp` に正規化されるため、Razor/Blazor ファイルを CLI と DB クエリ API の両方から引き続き検索できます。
