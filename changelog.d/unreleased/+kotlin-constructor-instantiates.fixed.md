---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/KotlinReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin same-file constructor calls now index as instantiations** - `User()` and `Profile(...)` calls to classes defined in the same Kotlin file now emit `instantiate` references, aligning constructor search with C# and Java while keeping PascalCase functions and annotations out of instantiation results.

## 日本語

- **Kotlin の同一ファイル constructor 呼び出しが instantiation としてインデックスされるようになりました** - 同じ Kotlin ファイル内で定義された class への `User()` や `Profile(...)` が `instantiate` 参照を出力し、C# / Java の constructor 検索に揃えつつ、PascalCase function や annotation は instantiation 結果に含めないようにしました。
