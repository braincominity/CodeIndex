---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Catch-clause exception types are now indexed for C#, Java, and Kotlin** - `catch (IOException ex)`, Java multi-catch, and Kotlin `catch (ex: IOException)` now emit `type_reference` rows for the exception type without treating catch variables as types.

## 日本語

- **C# / Java / Kotlin の catch 節の例外型がインデックスされるようになりました** - `catch (IOException ex)`、Java multi-catch、Kotlin の `catch (ex: IOException)` が例外型への `type_reference` を出力し、catch 変数は型として扱わないようにしました。
