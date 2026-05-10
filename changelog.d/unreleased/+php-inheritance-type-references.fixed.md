---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP inheritance clauses are now indexed as type references** — `extends` and `implements` targets now appear in reference results, including qualified and comma-separated interface names.

## 日本語

- **PHP の継承句を型参照として索引するようになりました** — `extends` / `implements` の対象を、完全修飾名やカンマ区切り interface 名も含めて reference 結果へ出します。
