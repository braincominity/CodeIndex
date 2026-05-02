---
category: fixed
affected:
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
  - README.md
---

## English

- **`--lang` now accepts common C# and Kotlin aliases** — query commands treat `c#` and `cs` as `csharp`, and `kt` and `kts` as `kotlin`, so language-filtered searches line up with the shorthand developers already use.

## 日本語

- **`--lang` が C# と Kotlin の一般的な別名を受け付けるようになりました** — クエリ系コマンドで `c#` と `cs` は `csharp`、`kt` と `kts` は `kotlin` として扱うため、普段使いの省略形でも言語フィルタ付き検索をそのまま使えます。
