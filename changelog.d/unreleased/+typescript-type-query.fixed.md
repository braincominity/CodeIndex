---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **TypeScript `typeof` and `keyof` type queries now surface as `type_reference` edges** — reference extraction now records simple TypeScript type-query targets, so `references`, `impact`, and related search paths can find declarations reached through `typeof Foo` and `keyof Foo` forms.

## 日本語

- **TypeScript の `typeof` / `keyof` 型クエリが `type_reference` エッジとして出るようになりました** — 参照抽出が簡単な TypeScript の型クエリ対象を記録するため、`typeof Foo` や `keyof Foo` 経由で到達する宣言を `references` / `impact` などの検索経路で見つけやすくなりました。
