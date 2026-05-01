---
category: fixed
affected:
  - src/CodeIndex/Database/DbSearchReader.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **T-SQL exact search now canonicalizes qualified identifiers before matching** — `search --exact-substring` now normalizes `sql` content and queries with the existing SQL name resolver, so bracketed or spaced identifiers such as `[sales] . [usp_Target]` match canonical queries like `sales.usp_Target` without broadening the behavior for other languages.

## 日本語

- **T-SQL の exact search が qualified identifier を正規化してから照合するようになりました** — `search --exact-substring` が既存の SQL name resolver を使って `sql` コンテンツとクエリを正規化するため、`[sales] . [usp_Target]` のような bracket / 空白付き識別子が `sales.usp_Target` のような canonical クエリに一致するようになり、他言語への挙動拡大は起きません。
