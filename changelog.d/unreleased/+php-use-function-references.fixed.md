---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP `use function` imports now emit references** — single, grouped, and mixed function imports now add function-name references, making imported functions discoverable by reference search without treating const imports as functions.

## 日本語

- **PHP の `use function` import を参照として索引するようになりました** — 単体、グループ、mixed group の function import が関数名の reference を追加し、const import を関数扱いせずに reference 検索で見つけられるようになります。
