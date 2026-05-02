---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Python from-imports now index qualified module paths for search** — `from package import submodule` and similar forms now add `package.submodule`-style import symbols in addition to the leaf names, so exact-name searches can find re-export and nested-import patterns more naturally.

## 日本語

- **Python の from-import でも修飾済みモジュール名を検索用に索引するようになりました** — `from package import submodule` のような形でも `package.submodule` 形式の import symbol を leaf 名と併せて追加するため、exact-name 検索で re-export や入れ子 import を見つけやすくなります。
