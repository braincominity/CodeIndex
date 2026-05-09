---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R `NAMESPACE` package imports now remain searchable** — `import(pkg)` directives emit a package reference while suppressing the directive helper call.

## 日本語

- **R の `NAMESPACE` パッケージ import が検索に残るようになりました** — `import(pkg)` ディレクティブは directive helper call を抑止しつつ、パッケージ参照を記録します。
