---
category: fixed
affected:
  - src/CodeIndex/Database/RepoMapBuilder.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **C# XAML code-behind files now surface as repo-map entrypoints** — `MainWindow.xaml.cs`, `MainPage.xaml.cs`, and `AppShell.xaml.cs` are treated as likely C# entry files so repo-map navigation and search-oriented exploration can land on common XAML app startup surfaces more reliably.

## 日本語

- **C# の XAML code-behind ファイルが repo map の entrypoint として出やすくなりました** — `MainWindow.xaml.cs`、`MainPage.xaml.cs`、`AppShell.xaml.cs` を C# の代表的なエントリーファイルとして扱うことで、repo map ベースの探索や検索から一般的な XAML アプリの起点へより確実に辿り着けるようになりました。
