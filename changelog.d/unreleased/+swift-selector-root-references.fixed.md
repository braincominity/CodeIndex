---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift `#selector` roots are now indexed as type references** — `#selector(ViewController.handleTap(_:))` and labeled forms such as `#selector(getter: ViewController.titleText)` expose `ViewController` without treating selector members as types.

## 日本語

- **Swift の `#selector` root を型参照として index するようにしました** — `#selector(ViewController.handleTap(_:))` や `#selector(getter: ViewController.titleText)` から `ViewController` を拾い、selector member は型として扱わないようにしました。
