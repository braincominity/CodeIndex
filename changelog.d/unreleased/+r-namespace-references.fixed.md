---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R namespace references now emit package-qualified reference edges** — `pkg::fun` and `pkg:::fun` are now indexed both as leaf `reference` rows and as package-qualified `pkg::fun` / `pkg:::fun` rows, so namespace-specific searches can disambiguate same-named symbols.

## 日本語

- **R の namespace 参照が package 修飾つきの reference edge として出るようになりました** — `pkg::fun` と `pkg:::fun` を leaf の `reference` 行に加えて `pkg::fun` / `pkg:::fun` の package 修飾名でも索引するため、同名 symbol を namespace 単位で見分けながら検索できます。
