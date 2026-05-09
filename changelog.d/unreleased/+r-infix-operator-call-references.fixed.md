---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R infix operator calls now appear in reference search** — operator functions such as `%>%` and `%||%` are emitted as call references when used in infix form.

## 日本語

- **R の infix operator 呼び出しが参照検索に出るようになりました** — `%>%` や `%||%` のような operator function を infix 形式で使った場合に call 参照として記録します。
