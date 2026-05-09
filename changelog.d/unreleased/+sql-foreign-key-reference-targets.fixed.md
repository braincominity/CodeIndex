---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL foreign-key `REFERENCES` targets are indexed as table references** — exact SQL reference search can now find tables that are only mentioned as foreign-key targets.

## 日本語

- **SQL foreign key の `REFERENCES` target を table reference として索引するようになりました** — foreign key の参照先としてだけ現れる table も、SQL の exact reference search で見つけられるようになりました。
