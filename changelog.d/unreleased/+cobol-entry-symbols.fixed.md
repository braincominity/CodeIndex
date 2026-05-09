---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Cobol.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **COBOL `ENTRY` names are now searchable as symbols** — alternate entry points now appear as function symbols for `symbols`, `definition`, and `inspect` workflows.

## 日本語

- **COBOL の `ENTRY` 名を symbol として検索可能にしました** — alternate entry point が function symbol として出るようになり、`symbols` / `definition` / `inspect` で辿れるようになりました。
