---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP static access now leaves searchable type references on the class side** — `Foo::bar()`, `Foo::CONST`, and `Foo::class` now emit `type_reference` edges for the class name, while `self::`, `static::`, and `parent::` stay suppressed so PHP class search picks up the useful static-access sites without adding pseudo-type noise.

## 日本語

- **PHP の静的アクセスでクラス側に検索可能な type reference を出すようになりました** — `Foo::bar()`、`Foo::CONST`、`Foo::class` からクラス名への `type_reference` を追加し、`self::`、`static::`、`parent::` は抑止したままにすることで、PHP のクラス検索で有用な static access を拾いつつ pseudo-type のノイズを増やさないようにしました。
