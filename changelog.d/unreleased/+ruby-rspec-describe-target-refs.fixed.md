---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby RSpec `describe` blocks now link subject constants** — `cdidx` records constants passed to `describe User do` and `RSpec.describe User do` while suppressing the DSL keyword as call noise.

## 日本語

- **Ruby RSpec の `describe` ブロックが対象定数へリンクするようになりました** — `cdidx` は `describe User do` や `RSpec.describe User do` の定数引数を記録し、DSLキーワード自体の call ノイズは抑えます。
