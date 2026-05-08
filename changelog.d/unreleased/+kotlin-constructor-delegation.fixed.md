---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/KotlinReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin secondary constructor delegation now resolves to the delegated type** — `constructor(...) : this(...)` now indexes a call to the enclosing class and `constructor(...) : super(...)` indexes a call to the superclass, while suppressing raw `constructor` / `this` / `super` keyword call noise to match the existing C# and Java constructor-chain behavior.

## 日本語

- **Kotlin のセカンダリコンストラクタ委譲が委譲先の型へ解決されるようになりました** — `constructor(...) : this(...)` は外側クラスへの call、`constructor(...) : super(...)` は superclass への call としてインデックスされ、raw な `constructor` / `this` / `super` keyword call ノイズは抑止されるため、C# / Java の既存のコンストラクタ連鎖挙動と揃いました。
