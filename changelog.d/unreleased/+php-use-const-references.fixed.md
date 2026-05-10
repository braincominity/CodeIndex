---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP `use const` imports now emit references** — single, grouped, and mixed constant imports now add constant-name references without treating function imports as constants.

## 日本語

- **PHP の `use const` import を参照として索引するようになりました** — 単体、グループ、mixed group の const import が定数名の reference を追加し、function import を定数扱いしなくなります。
