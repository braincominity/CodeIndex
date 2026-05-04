---
category: internal
affected:
  - src/CodeIndex/Cli/IndexCommandRunner.cs
---

## English

- **Rebuild scans avoid unnecessary unchanged-file lookups** — `cdidx index --rebuild` now skips the per-file reuse query after dropping the database, reducing rebuild-time database work without changing incremental indexing behavior.

## 日本語

- **rebuild scan で不要な unchanged-file lookup を避けるようにしました** — `cdidx index --rebuild` はデータベース全削除後のファイルごとの再利用クエリを省き、incremental index の挙動は変えずに rebuild 時の DB 作業を減らします。
