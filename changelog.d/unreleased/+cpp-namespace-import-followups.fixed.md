---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C++ `using namespace` imports now ignore trailing comments** — namespace import targets are normalized before storage, so `using namespace std; // comment` and similar forms still index as `std` instead of capturing the comment suffix. The older regex fallback for C++ namespace import rows was removed in favor of the helper-based path.

## 日本語

- **C++ の `using namespace` import が末尾コメントを無視するようになりました** — namespace import の target を保存前に正規化するため、`using namespace std; // comment` のような形もコメント部分を含めず `std` として索引されます。あわせて、C++ namespace import 行に対する古い regex fallback は helper ベースの経路へ整理しました。
