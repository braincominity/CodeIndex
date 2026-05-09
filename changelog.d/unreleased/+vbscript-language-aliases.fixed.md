---
category: fixed
affected:
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - src/CodeIndex/Database/DbReader.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **VBScript language aliases now map to Visual Basic search** — `vbs` and `vbscript` normalize to `vb`, matching the existing `.vbs` file-language detection.

## 日本語

- **VBScript の language alias が Visual Basic 検索へ対応しました** — `.vbs` の既存ファイル検出に合わせて、`vbs` と `vbscript` を `vb` に正規化します。
