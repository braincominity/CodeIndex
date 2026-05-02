---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP static access now emits namespace-qualified type references when the target is qualified** — fully-qualified forms like `\App\Models\Config::class` and `\App\Models\Config::rebuild()` now add a `type_reference` edge for `App\Models\Config` as well as the short `Config`, which makes class navigation and search more precise for namespaced PHP code.

## 日本語

- **PHP の静的アクセスで、修飾済みターゲットに namespace-qualified な type reference を追加しました** — `\App\Models\Config::class` や `\App\Models\Config::rebuild()` のような fully-qualified 形では、短い `Config` に加えて `App\Models\Config` への `type_reference` も出すようにし、名前空間付き PHP コードでのクラス検索とナビゲーションをより正確にしました。
