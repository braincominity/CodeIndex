---
category: changed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/TypeScriptReferenceExtractor.cs
  - src/CodeIndex/Indexer/KotlinReferenceExtractor.cs
  - src/CodeIndex/Indexer/SwiftReferenceExtractor.cs
  - src/CodeIndex/Indexer/RustReferenceExtractor.cs
  - src/CodeIndex/Indexer/TypedLanguageReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Expanded non-C# reference extraction granularity** — TypeScript, Kotlin, Swift, and Rust now index more structural `type_reference` edges from declarations, inheritance/implementation clauses, generic bounds, type tests/casts, and typed variables so symbol workflows can find dependencies with finer language-aware precision.

## 日本語

- **C# 以外の参照抽出粒度を拡張しました** — TypeScript、Kotlin、Swift、Rust で宣言、継承/実装句、generic 境界、型テスト/キャスト、型付き変数からより多くの構造的な `type_reference` エッジを索引化し、シンボル系ワークフローが依存関係をより細かい言語別精度で辿れるようになりました。
