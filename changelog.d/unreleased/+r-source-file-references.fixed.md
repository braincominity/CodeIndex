---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R `source()` calls now record the sourced file path as a reference** — explicit source paths appear in reference search while the helper call itself is suppressed.

## 日本語

- **R の `source()` 呼び出しが source 先ファイルパスを参照として記録するようになりました** — 明示された source 先パスを参照検索に出し、helper call 自体は抑止します。
