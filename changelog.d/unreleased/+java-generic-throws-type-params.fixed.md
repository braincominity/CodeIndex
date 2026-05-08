---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/JavaReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Java generic throws clauses no longer emit type-parameter self references** - `public <E extends Failure> void run() throws E` keeps the real bound type while suppressing the generic exception parameter.

## 日本語

- **Java の generic throws 句が型パラメータ自身を型参照として出さないようになりました** - `public <E extends Failure> void run() throws E` では実際の bound 型を残しつつ、generic exception parameter のノイズを抑制します。
