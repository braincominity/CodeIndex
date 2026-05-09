---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ explicit template instantiations are indexed as type references** — declarations such as `extern template class std::vector<Widget>;` now expose the instantiated template and argument types.

## 日本語

- **C++ の explicit template instantiation を type reference として index するようになりました** — `extern template class std::vector<Widget>;` のような宣言が、instantiation対象templateと型引数を出します。
