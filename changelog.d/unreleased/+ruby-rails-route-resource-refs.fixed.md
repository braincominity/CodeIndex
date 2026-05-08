---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby Rails route declarations now index resource names** — `cdidx` records names passed to `resources :articles` and `resource :profile` without indexing route option keys as references.

## 日本語

- **Ruby Rails の route 宣言がresource名を索引するようになりました** — `cdidx` は `resources :articles` や `resource :profile` の名前を記録し、routeオプションキーは参照として扱いません。
