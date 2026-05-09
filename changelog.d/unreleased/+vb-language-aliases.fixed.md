---
category: fixed
affected:
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - src/CodeIndex/Database/DbReader.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **Visual Basic language aliases now accept hyphen and underscore spellings** — `visual-basic` and `visual_basic` normalize to `vb` in CLI and database search paths.

## 日本語

- **Visual Basic の language alias がハイフン・アンダースコア表記に対応しました** — CLI と database search 経路で `visual-basic` と `visual_basic` を `vb` に正規化します。
