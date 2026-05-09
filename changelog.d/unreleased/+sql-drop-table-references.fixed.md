---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL `DROP TABLE` statements now surface table references** — exact SQL reference search can now find teardown migrations that drop one or more T-SQL tables, including `IF EXISTS` forms.

## 日本語

- **SQL の `DROP TABLE` が table reference として出るようになりました** — `IF EXISTS` を含む T-SQL の単一または複数 table 削除 migration も、SQL の exact reference search で見つけられるようになりました。
