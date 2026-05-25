---
category: internal
affected:
  - src/CodeIndex/Cli/IndexCommandRunner.DryRun.cs
  - src/CodeIndex/Cli/IndexCommandRunner.cs
---

## English

- **Split IndexCommandRunner internals without behavior changes** — moved index option validation, watch conflict handling, rebuild conflict handling, rebuild confirmation, dry-run execution, and dry-run candidate resolution out of the oversized index command runner, with dry-run code now isolated in a partial file.

## 日本語

- **IndexCommandRunner の内部構造を挙動変更なしで分割しました** — 巨大な index command runner から index option validation、watch conflict handling、rebuild conflict handling、rebuild confirmation、dry-run execution、dry-run candidate resolution を切り出し、dry-run code を partial file に分離しました。
