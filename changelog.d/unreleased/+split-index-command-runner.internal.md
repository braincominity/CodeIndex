---
category: internal
affected:
  - src/CodeIndex/Cli/IndexCommandRunner.cs
---

## English

- **Split IndexCommandRunner internals without behavior changes** — moved index option validation, watch conflict handling, rebuild conflict handling, rebuild confirmation, and dry-run execution out of the oversized index command runner.

## 日本語

- **IndexCommandRunner の内部構造を挙動変更なしで分割しました** — 巨大な index command runner から index option validation、watch conflict handling、rebuild conflict handling、rebuild confirmation、dry-run execution を切り出しました。
