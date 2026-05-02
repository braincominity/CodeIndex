---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP object-member navigation now emits searchable references** — `->member` and `?->member` accesses are now indexed as `reference` edges, while method calls continue to flow through the existing PHP call matcher. This makes property and chained member lookups searchable without adding duplicate call noise.

## 日本語

- **PHP の object-member navigation が検索可能な reference として索引されるようになりました** — `->member` と `?->member` のアクセスを `reference` edge として index し、method call は従来の PHP call matcher にそのまま流すようにしました。これにより、property や連鎖した member lookup を重複した call ノイズなしで検索できます。
