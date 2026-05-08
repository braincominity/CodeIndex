---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust glob imports now reference their parent module** — `use crate::prelude::*;` records `prelude` instead of dropping the import target at `*`.

## 日本語

- **Rust glob import が親 module を reference として出すようになりました** — `use crate::prelude::*;` で `*` に到達して捨てず、`prelude` を記録します。
