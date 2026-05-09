---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R `NAMESPACE` S3 method directives now create searchable references** — `S3method(generic, class)` records the generated `generic.class` method name plus its generic and class parts, while suppressing the directive helper call.

## 日本語

- **R の `NAMESPACE` S3 method ディレクティブが検索可能な参照を生成するようになりました** — `S3method(generic, class)` は生成される `generic.class` メソッド名と generic / class 部分を記録し、directive helper call は抑止します。
