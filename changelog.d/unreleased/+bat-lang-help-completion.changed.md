---
category: changed
affected:
  - src/CodeIndex/Cli/ConsoleUi.cs
  - tests/CodeIndex.Tests/ConsoleUiTests.cs
---

## English

- **Windows batch aliases now appear in CLI help and completions** — `--lang` help now mentions `bat` and `cmd` as aliases for `batch`, and shell completion lists them alongside the canonical language name so users can discover the shorthand directly from the CLI.

## 日本語

- **Windows バッチの alias が CLI help と補完候補に表示されるようになりました** — `--lang` の help に `batch` の別名として `bat` と `cmd` を明記し、シェル補完でも canonical な言語名と並んで候補に出すことで、CLI から短縮表記を見つけやすくしました。
