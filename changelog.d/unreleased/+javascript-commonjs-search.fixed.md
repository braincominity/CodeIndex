---
category: fixed
affected:
  - src/CodeIndex/Database/DbSymbolReader.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **JavaScript CommonJS export queries now resolve to leaf symbols** — exact symbol searches now normalize `module.exports.foo` and `exports.bar` to the canonical leaf name, so CommonJS export surfaces are searchable with the same spelling users see in extracted symbols.

## 日本語

- **JavaScript の CommonJS export クエリが leaf シンボルへ解決されるようになりました** — exact symbol search で `module.exports.foo` や `exports.bar` を canonical な leaf 名へ正規化するため、CommonJS export surface を抽出済みシンボルと同じ綴りで検索できます。
