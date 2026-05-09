---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R S4 slot accesses now surface in reference search** — expressions such as `model@coefficients`, backtick slot names, and backtick receiver names emit qualified and leaf references.

## 日本語

- **R の S4 slot access が参照検索に出るようになりました** — `model@coefficients`、バッククォート付き slot 名、バッククォート付き receiver 名から qualified / leaf の両方の参照を記録します。
