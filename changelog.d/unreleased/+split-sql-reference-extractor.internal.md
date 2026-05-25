---
category: internal
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.State.cs
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
---

## English

- **Split SQL statement reference internals without behavior changes** — moved per-statement CTE/call-suppression setup plus create-index, alter/drop-index, object lifecycle, drop-object, alter-object, maintenance target emission, and MERGE action column emission into focused helpers outside the oversized SQL reference emission method, and split SQL extractor state into a partial file.

## 日本語

- **SQL statement 参照内部処理を挙動変更なしで分割しました** — 巨大な SQL 参照 emit メソッドから statement ごとの CTE / call suppression 準備と、create-index / alter/drop-index / object lifecycle / drop-object / alter-object / maintenance target emit / MERGE action column emit を focused helper へ切り出し、SQL extractor state を partial file に分離しました。
