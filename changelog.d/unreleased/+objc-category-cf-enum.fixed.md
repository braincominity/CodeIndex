---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Objective-C now exposes category names and CoreFoundation enum macros in symbol search** — `SymbolExtractor` emits `Foo(Category)`-style category symbols for `@interface` / `@implementation` declarations and indexes `CF_ENUM` / `CF_OPTIONS` typedefs alongside the existing Apple enum macros, so category-specific and CoreFoundation type names now show up in `symbols` and definition-oriented views.

## 日本語

- **Objective-C でカテゴリ名と CoreFoundation の enum マクロが symbol search に出るようになりました** — `SymbolExtractor` が `@interface` / `@implementation` 宣言から `Foo(Category)` 形式のカテゴリ symbol を出力し、既存の Apple enum マクロに加えて `CF_ENUM` / `CF_OPTIONS` の typedef も索引するため、カテゴリ単位の名前や CoreFoundation 型名が `symbols` や definition 系の表示に現れるようになりました。
