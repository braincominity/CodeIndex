---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **CSS custom property registrations are now indexed** — `@property --accent-color` is surfaced as a `property`, so registered custom properties are easier to find alongside inline `--tokens` in CSS projects.

## 日本語

- **CSS のカスタムプロパティ登録が索引されるようになりました** — `@property --accent-color` が `property` として現れるため、登録済みのカスタムプロパティを CSS プロジェクトで inline の `--token` とあわせて見つけやすくなります。
