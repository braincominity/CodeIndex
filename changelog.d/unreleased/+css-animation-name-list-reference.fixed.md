---
category: fixed
affected:
  - src/CodeIndex/Indexer/CssReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **CSS `animation-name` lists now reference every keyframe name** — comma-separated `animation-name` values such as `fade-in, none, slide-up` now emit references for each real keyframe name instead of only the first entry.

## 日本語

- **CSS の `animation-name` リストが全 keyframe 名を参照化するようになりました** — `fade-in, none, slide-up` のようなカンマ区切りの `animation-name` 値で、先頭だけでなく実際の keyframe 名すべてを reference として発行します。
