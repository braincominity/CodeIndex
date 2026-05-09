---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R `NAMESPACE` import/export directives now produce symbol references** — `importFrom(pkg, name)` records both `pkg::name` and `name`, while `export(...)`, `exportClasses(...)`, and `exportMethods(...)` record exported names without adding noisy directive calls.

## 日本語

- **R の `NAMESPACE` import/export ディレクティブがシンボル参照を生成するようになりました** — `importFrom(pkg, name)` は `pkg::name` と `name` の両方を記録し、`export(...)` / `exportClasses(...)` / `exportMethods(...)` はノイズになるディレクティブ call を出さずに export 対象名を記録します。
