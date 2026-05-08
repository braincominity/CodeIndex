---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **JavaScript/TypeScript dynamic import search no longer treats `.import()` methods as runtime imports** — property and private-method calls such as `client.import("./x")`, `client?.import("./x")`, and `this.#import("./x")` no longer create module `import` symbols.

## 日本語

- **JavaScript/TypeScript の dynamic import 検索が `.import()` メソッドを runtime import と扱わないようになりました** — `client.import("./x")`、`client?.import("./x")`、`this.#import("./x")` のような property / private method 呼び出しでは module の `import` シンボルを作らなくなりました。
