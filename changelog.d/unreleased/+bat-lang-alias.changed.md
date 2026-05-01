---
category: changed
affected:
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Windows batch `--lang` aliases now map to `batch`** — query commands that accept `--lang` now canonicalize `bat` and `cmd` to `batch`, so Windows batch searches keep working with the shorthand names users commonly type.

## 日本語

- **Windows バッチ向けの `--lang` 別名が `batch` に正規化されるようになりました** — `--lang` を受け付けるクエリ系コマンドで `bat` と `cmd` を `batch` に正規化するため、Windows バッチ検索でもユーザーがよく入力する短縮名のまま検索できます。
