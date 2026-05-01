---
category: fixed
affected:
  - src/CodeIndex/Database/RepoMapBuilder.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **VB.NET repo maps now recognize common startup file names** — `map` / `repo map` now treats `Main.vb` and `Module.vb` as entrypoint file fallbacks in addition to `Program.vb`, `Module1.vb`, and `App.vb`, so VB projects with renamed startup modules are easier to surface.

## 日本語

- **VB.NET の repo map が一般的な起動ファイル名を認識するようになりました** — `map` / `repo map` は `Program.vb`、`Module1.vb`、`App.vb` に加えて `Main.vb` と `Module.vb` も entrypoint の file fallback として扱うため、名前を変更した VB の起動モジュールを見つけやすくなります。
