---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Swift multiple raw-value enum cases are now indexed individually** — `case accepted = 202, gone = 410` now emits both `accepted` and `gone` symbols with their own raw values.

## 日本語

- **Swift の複数raw-value enum caseを個別に index するようにしました** — `case accepted = 202, gone = 410` から `accepted` と `gone` の両方を、それぞれのraw値付きで記録します。
