---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **Rust raw identifiers now search by canonical names** — Rust symbol extraction strips `r#` from declared names before indexing, so `r#type` is stored and searched as `type`. Symbol search queries also keep matching the canonical name when users include the raw prefix.

## 日本語

- **Rust の raw identifier が canonical 名で検索できるようになりました** — Rust のシンボル抽出では宣言名から `r#` を取り除いて index するため、`r#type` は `type` として保存・検索されます。検索クエリ側も raw prefix を付けた入力を canonical 名に合わせて扱います。
