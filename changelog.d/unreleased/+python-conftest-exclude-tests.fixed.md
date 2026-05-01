---
category: fixed
affected:
  - src/CodeIndex/Database/DbReader.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **`--exclude-tests` now filters Python `conftest.py` helper files** — search commands treat `conftest.py` as test support, so Python projects can drop fixture-only files from search results along with regular test modules.

## 日本語

- **`--exclude-tests` が Python の `conftest.py` 補助ファイルも除外するようになりました** — 検索コマンドは `conftest.py` を test support とみなすため、Python プロジェクトでは通常のテストモジュールと同様に fixture 専用ファイルを検索結果から外せます。
