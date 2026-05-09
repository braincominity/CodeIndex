---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ standard factory template arguments are indexed** — `make_unique`, `make_shared`, and `make_optional` calls now expose their template type arguments as type references.

## 日本語

- **C++ 標準ファクトリの template 型引数を index するようになりました** — `make_unique`、`make_shared`、`make_optional` 呼び出しが template 型引数を type reference として出します。
