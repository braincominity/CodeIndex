---
category: fixed
affected:
  - src/CodeIndex/Database/DbReader.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **SQL Server language aliases now resolve to `sql` in query commands** — `search`, `symbols`, `references`, `callers`, `callees`, and `impact` now treat `tsql`, `t-sql`, `mssql`, and `sqlserver` as the same SQL language filter, so T-SQL users can reach indexed files with the spellings they actually type.

## 日本語

- **SQL Server の言語別名がクエリコマンドで `sql` に正規化されるようになりました** — `search`、`symbols`、`references`、`callers`、`callees`、`impact` が `tsql`、`t-sql`、`mssql`、`sqlserver` を同じ SQL 言語フィルタとして扱うため、T-SQL 利用者が実際に入力しがちな綴りでインデックス済みファイルに到達できます。
