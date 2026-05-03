---
category: fixed
issues:
  - 1364
affected:
  - src/CodeIndex/Database/DbContext.cs
---

## English

- **SQLite connection setup now enables `busy_timeout` before the first setup PRAGMA** — `DbContext` applies the timeout immediately after opening the connection, before registering functions or switching journal mode, so concurrent read opens are less likely to fail with transient `database is locked` errors on macOS.

## 日本語

- **SQLite の接続初期化で最初の setup PRAGMA より前に `busy_timeout` を有効化するようにしました** — `DbContext` は接続を開いた直後、関数登録や journal mode 切り替えの前にタイムアウトを設定するため、macOS での並行 read open が一時的な `database is locked` で失敗しにくくなります。
