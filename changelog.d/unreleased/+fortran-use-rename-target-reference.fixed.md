---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran `use ... only:` rename targets now appear in reference search** — `local => remote` imports index the remote symbol as well as the local alias.

## 日本語

- **Fortran の `use ... only:` rename 元が参照検索に出るようになりました** — `local => remote` 形式の import で、local alias だけでなく remote symbol も索引します。
