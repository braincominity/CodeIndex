---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP attributes now emit type references** — attribute classes such as `#[Route(...)]` and `#[\App\Http\Middleware\RequiresAuth]` now appear as `type_reference` entries without treating named arguments as types.

## 日本語

- **PHP attributes を型参照として索引するようになりました** — `#[Route(...)]` や `#[\App\Http\Middleware\RequiresAuth]` のような attribute class を `type_reference` として出し、名前付き引数は型として扱いません。
