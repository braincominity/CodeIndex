---
category: fixed
affected:
  - src/CodeIndex/Cli/SearchSnippetFormatter.cs
  - tests/CodeIndex.Tests/SearchSnippetFormatterTests.cs
---

## English

- **C# search snippets now normalize verbatim qualified names in all modes** — search excerpt generation now treats `@Foo.@Bar` and `Foo.Bar` as the same C# path even outside exact mode, so verbatim-qualified matches are highlighted consistently instead of depending on the source spelling.

## 日本語

- **C# の search snippet で verbatim 修飾名を全モードで正規化するようになりました** — search excerpt の生成時に exact 以外でも `@Foo.@Bar` と `Foo.Bar` を同じ C# の経路として扱うため、verbatim 修飾名の一致が source の綴りに左右されず一貫してハイライトされます。
