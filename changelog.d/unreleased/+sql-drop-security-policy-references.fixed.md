---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP SECURITY POLICY targets are indexed as references** — exact SQL reference search can now find security policies mentioned by `DROP SECURITY POLICY`.

## 日本語

- **SQL DROP SECURITY POLICY の target を reference として索引するようになりました** — `DROP SECURITY POLICY` で指定される security policy も、SQL の exact reference search で見つけられるようになりました。
