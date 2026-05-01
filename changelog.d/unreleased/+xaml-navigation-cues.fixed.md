---
category: fixed
affected:
  - src/CodeIndex/Database/RepoMapBuilder.cs
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C# XAML navigation now surfaces more app entrypoints and handler targets** — repo-map entrypoint hints now include additional common code-behind filenames such as `Shell.xaml.cs`, `ContentPage.xaml.cs`, `ContentView.xaml.cs`, `Window.xaml.cs`, and `UserControl.xaml.cs`, and XAML event handler attributes like `Clicked` and `SelectionChanged` are indexed as `function` symbols so searches can jump from markup to code-behind more naturally.

## 日本語

- **C# XAML のナビゲーションで entrypoint と handler の両方がより見つかりやすくなりました** — repo map の entrypoint 候補に `Shell.xaml.cs`、`ContentPage.xaml.cs`、`ContentView.xaml.cs`、`Window.xaml.cs`、`UserControl.xaml.cs` などを追加し、`Clicked` や `SelectionChanged` のような XAML event handler 属性も `function` シンボルとして索引化することで、markup から code-behind へより自然に辿れるようになりました。
