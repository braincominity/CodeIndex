---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C++ `#import` directives are indexed as imports** — Objective-C++ style `#import <UIKit/UIKit.h>` and quoted imports now appear in symbol and import searches alongside `#include`.

## 日本語

- **C++ の `#import` directive を import として index するようになりました** — Objective-C++ 形式の `#import <UIKit/UIKit.h>` やquoted importが、`#include` と同じく symbol / import search に出ます。
