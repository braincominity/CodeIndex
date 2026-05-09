---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Qualified C++ template braced constructions expose type arguments** — `std::optional<Widget>{}` and `ns::Result<Success, Error>{}` now contribute their template arguments to reference search without indexing namespace qualifiers.

## 日本語

- **修飾付き C++ template の braced construction で型引数を参照抽出するようになりました** — `std::optional<Widget>{}` や `ns::Result<Success, Error>{}` から、namespace 修飾子ではなく template 型引数を reference search に出します。
