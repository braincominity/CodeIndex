---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL `ALTER TABLE` statements now surface table references** — `references`, callers/search-adjacent graph readers, and exact SQL name matching can now find tables that are only touched by T-SQL schema migrations.

## 日本語

- **SQL の `ALTER TABLE` が table reference として出るようになりました** — T-SQL の schema migration だけで触られる table も、`references` や関連 graph reader、SQL exact name matching で見つけられるようになりました。
