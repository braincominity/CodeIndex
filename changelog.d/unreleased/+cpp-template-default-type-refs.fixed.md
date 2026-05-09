---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ template parameter default types are indexed** — Defaults such as `typename T = Widget` and `class Alloc = ns::Allocator<Widget>` now contribute their referenced types to search results.

## 日本語

- **C++ template parameter の default type を index するようになりました** — `typename T = Widget` や `class Alloc = ns::Allocator<Widget>` の default type が reference search に出るようになります。
