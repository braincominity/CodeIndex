---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift generic invocation arguments are now indexed as type references** — calls like `decode<User>(data)` and `decode<Result<User, Failure>>(data)` expose their generic type arguments without indexing declaration parameters from `func decode<T>`.

## 日本語

- **Swift generic 呼び出しの型引数を型参照として index するようにしました** — `decode<User>(data)` や `decode<Result<User, Failure>>(data)` の型引数を拾い、`func decode<T>` 側の宣言パラメータは型参照にしません。
