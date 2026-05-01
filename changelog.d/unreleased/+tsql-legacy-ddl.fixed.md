---
category: fixed
issues: []
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **T-SQL `RULE` and `DEFAULT` definitions now show up in symbol search** — `CREATE RULE` and `CREATE DEFAULT` rows are now indexed as SQL symbols, so older SQL Server codebases can find those legacy object names instead of skipping them entirely.

## 日本語

- **T-SQL の `RULE` と `DEFAULT` 定義がシンボル検索に出るようになりました** — `CREATE RULE` と `CREATE DEFAULT` の行を SQL シンボルとして索引付けするため、古い SQL Server コードベースでもこれらの legacy オブジェクト名を取りこぼさず検索できます。
