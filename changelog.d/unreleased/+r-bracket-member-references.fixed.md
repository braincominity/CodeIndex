---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R `[[...]]` member accesses now surface in reference search** — string-keyed lookups such as `data[["value"]]` and `input[['go']]` are indexed as the same qualified references as `$` access.

## 日本語

- **R の `[[...]]` member access が参照検索に出るようになりました** — `data[["value"]]` や `input[['go']]` のような文字列キー access を `$` と同じ qualified reference として索引します。
