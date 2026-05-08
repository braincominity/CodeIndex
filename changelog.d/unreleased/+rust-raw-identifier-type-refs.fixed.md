---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust raw identifiers now normalize in type references** — type-position references such as `r#type` and `crate::r#type` are indexed under the usable symbol name instead of leaking a phantom `r` segment.

## 日本語

- **Rust raw identifier を型参照でも正規化するようになりました** — `r#type` や `crate::r#type` のような型位置の参照を、phantom な `r` ではなく実際に検索できるシンボル名でインデックスします。
