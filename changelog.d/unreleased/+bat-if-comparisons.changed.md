---
category: changed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - README.md
---

## English

- **Batch `if` comparison forms now contribute `goto` / `call` targets** — reference extraction broadens batch jump parsing beyond the common `errorlevel` / `defined` / `exist` / `cmdextversion` prefixes, so comparison-style `if` lines such as `if /i "%MODE%"=="release" goto :Release` are indexed as call targets too.

## 日本語

- **Batch の `if` 比較式でも `goto` / `call` ターゲットを拾うようになりました** — reference extraction が batch の jump 解析を、従来の `errorlevel` / `defined` / `exist` / `cmdextversion` だけでなく比較式ベースの `if` にも広げたため、`if /i "%MODE%"=="release" goto :Release` のような行も call target として索引されます。
