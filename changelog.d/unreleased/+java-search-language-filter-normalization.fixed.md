---
category: fixed
affected:
  - src/CodeIndex/Database/DbReader.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Java search now normalizes canonical `Java` language filters too** — the database query layer now treats `Java` the same as `java` and `jav`, so direct search callers keep matching Java rows even when they pass the canonical language name in mixed case.

## 日本語

- **Java 検索で canonical な `Java` 言語フィルタも正規化するようになりました** — データベースのクエリ層で `Java` を `java` / `jav` と同一視するため、直接 search を呼ぶ側が canonical 名を大文字小文字混在で渡しても Java 行を取りこぼしません。
