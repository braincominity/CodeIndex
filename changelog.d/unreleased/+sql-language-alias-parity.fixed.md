---
category: fixed
affected:
  - src/CodeIndex/Database/DbReader.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **SQL-family language aliases now normalize consistently across the query stack** — `transact-sql`, `transact sql`, and the surfaced `transactsql` spelling now resolve to `sql` the same way `tsql`, `mssql`, and `sqlserver` do, so SQL users get the same behavior whether the spelling reaches the CLI parser or the database reader directly.

## 日本語

- **SQL 系の言語別名がクエリスタック全体で一貫して正規化されるようになりました** — `transact-sql`、`transact sql`、そして表示される `transactsql` 表記も `tsql`、`mssql`、`sqlserver` と同様に `sql` へ解決されるため、CLI パーサー経由でも DB reader 直呼びでも同じ挙動になります。
