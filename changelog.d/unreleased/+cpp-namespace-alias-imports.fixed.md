---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **C++ namespace aliases and `using namespace` directives now show up in `symbols`** — `namespace fs = std::filesystem;` and `using namespace std::chrono_literals;` are now indexed as `import` symbols alongside existing `using` and `typedef` aliases, so namespace import forms are searchable instead of disappearing into raw text.

## 日本語

- **C++ の namespace エイリアスと `using namespace` 宣言が `symbols` に現れるようになりました** — `namespace fs = std::filesystem;` や `using namespace std::chrono_literals;` を既存の `using` / `typedef` エイリアスと同様に `import` シンボルとして索引するため、名前空間導入の形も raw text に埋もれず検索できます。
