---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **CSS search now keeps class selectors visible inside mixed selector lists** — `ReferenceExtractor` now scans comma-separated selector parts individually, so nested selectors like `button, .card { ... }` still surface `.card` as a searchable reference instead of dropping it when the list starts with an element selector.

## 日本語

- **CSS 検索で mixed selector list 内の class selector を取りこぼさなくなりました** — `ReferenceExtractor` が comma 区切りの selector 部分を個別に見るようになり、`button, .card { ... }` のような nested selector でも `.card` を検索可能な reference として拾えるようになりました。selector list が要素セレクタから始まっても落ちません。
