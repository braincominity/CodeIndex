---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/KotlinReferenceExtractor.cs
---

## English

- **Kotlin generic parameter suppression now only uses declaration generic clauses** - type arguments such as `Box<User>` and `Producer<out Payload>` are no longer mistaken for generic parameter declarations.

## 日本語

- **Kotlin の generic parameter 抑制が宣言 generic 句だけを見るようになりました** - `Box<User>` や `Producer<out Payload>` のような型引数を generic parameter 宣言と誤認しないようにしました。
