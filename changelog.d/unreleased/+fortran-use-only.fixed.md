---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran `use ... only:` imports now appear in reference search** — imported names listed after `only:` are indexed as type references alongside the module name, so dependency searches can find explicit Fortran imports.

## 日本語

- **Fortran の `use ... only:` import が参照検索に出るようになりました** — `only:` の後に列挙された import 名を module 名と合わせて型参照として索引し、明示的な Fortran import を依存検索で見つけられるようにしました。
