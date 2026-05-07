---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.TypeReferences.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Java and Kotlin documentation links now emit searchable type references** — Javadoc links such as `{@link User#save()}` / `@see Helper` and KDoc links such as `[User.name]` now populate `type_reference` rows on the documented symbol, matching the existing C# XML-doc `cref` search behavior.

## 日本語

- **Java / Kotlin のドキュメントリンクを型参照として検索できるようになりました** — `{@link User#save()}` / `@see Helper` のような Javadoc link と `[User.name]` のような KDoc link を、既存の C# XML-doc `cref` と同じく documented symbol 上の `type_reference` として発行します。
