---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C++20 module imports are searchable as import symbols** — `import std;`, partition imports, and header-unit imports now appear in symbol results instead of only preprocessor includes being visible.

## 日本語

- **C++20 module import を import symbol として検索できるようになりました** — `import std;`、partition import、header-unit import が symbol 結果に出るようになり、preprocessor include だけが見える状態を解消しました。
