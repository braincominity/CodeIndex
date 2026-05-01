---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - DEVELOPER_GUIDE.md
  - README.md
---

## English

- **T-SQL assembly and XML schema collection declarations now surface as searchable symbols** — the SQL extractor now indexes `CREATE/ALTER ASSEMBLY` and `CREATE/ALTER XML SCHEMA COLLECTION` rows, so SQL Server deployment scripts for those object types are no longer skipped by `symbols` and `definition`.

## 日本語

- **T-SQL の assembly と XML schema collection 宣言が検索可能な symbol として表面化するようになりました** — SQL extractor が `CREATE/ALTER ASSEMBLY` と `CREATE/ALTER XML SCHEMA COLLECTION` を index するため、これらのオブジェクト種別の SQL Server 配備スクリプトが `symbols` / `definition` で取りこぼされなくなりました。
