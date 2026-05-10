---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP group `use` imports now emit type references** — grouped class imports such as `use App\Domain\{User, Team\Member};` now appear in reference search results, while grouped function and const imports stay excluded from type references.

## 日本語

- **PHP のグループ `use` import を型参照として索引するようになりました** — `use App\Domain\{User, Team\Member};` のような grouped class import が reference 検索結果に出るようになり、grouped function / const import は引き続き type reference から除外します。
