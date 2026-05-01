---
category: fixed
affected:
  - src/CodeIndex/Database/RepoMapBuilder.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **VB.NET repo maps now recognize more common startup file names** — `map` / `repo map` now treats `Form1.vb` and `App.xaml.vb` as entrypoint file fallbacks too, extending the earlier VB startup-file follow-up so WinForms and WPF projects surface more naturally.

## 日本語

- **VB.NET の repo map がより一般的な起動ファイル名を認識するようになりました** — `map` / `repo map` は `Form1.vb` と `App.xaml.vb` も entrypoint の file fallback として扱うため、前回の VB startup-file follow-up を拡張し、WinForms / WPF プロジェクトをより自然に見つけやすくなります。
