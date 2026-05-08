---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++20 module imports participate in references and dependency queries** — `import std;`, partition imports, and header-unit imports now emit type-reference rows like preprocessor includes.

## 日本語

- **C++20 module import が references / dependency query に反映されるようになりました** — `import std;`、partition import、header-unit import が preprocessor include と同様に type-reference 行を出します。
