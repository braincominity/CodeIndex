---
category: internal
affected:
  - src/CodeIndex/Cli/IndexCommandRunner.DryRun.cs
  - src/CodeIndex/Cli/IndexCommandRunner.Maintenance.cs
  - src/CodeIndex/Cli/IndexCommandRunner.Parse.cs
  - src/CodeIndex/Cli/IndexCommandRunner.Validation.cs
  - src/CodeIndex/Cli/IndexCommandRunner.cs
---

## English

- **Split IndexCommandRunner internals without behavior changes** — moved index option validation, watch conflict handling, rebuild conflict handling, rebuild confirmation, dry-run execution, dry-run candidate resolution, index argument parsing, maintenance command runners, and maintenance argument parsing out of the oversized index command runner, with dry-run, parse, validation, and maintenance code now isolated in partial files.

## 日本語

- **IndexCommandRunner の内部構造を挙動変更なしで分割しました** — 巨大な index command runner から index option validation、watch conflict handling、rebuild conflict handling、rebuild confirmation、dry-run execution、dry-run candidate resolution、index argument parsing、maintenance command runner、maintenance argument parsing を切り出し、dry-run / parse / validation / maintenance code を partial file に分離しました。
