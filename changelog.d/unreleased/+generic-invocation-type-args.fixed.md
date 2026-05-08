---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.TypeReferences.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C#, Java, and Kotlin generic invocation type arguments are now searchable as type references** — generic constructor/function calls such as `new List<Payload>()`, `Collections.<Result>emptyList()`, and `read<Result>()` now emit `type_reference` rows for their type arguments after the existing call/instantiate guards accept the invocation.

## 日本語

- **C# / Java / Kotlin の generic 呼び出し型引数を型参照として検索できるようになりました** — `new List<Payload>()`、`Collections.<Result>emptyList()`、`read<Result>()` のような generic constructor / function 呼び出しで、既存の call / instantiate guard が受理した呼び出しの型引数を `type_reference` として発行します。
