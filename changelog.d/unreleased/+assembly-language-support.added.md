---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Assembly.cs
  - src/CodeIndex/Indexer/References/Languages/AssemblyReferenceExtractor.cs
  - USER_GUIDE.md
---

## English

- **Assembly files now support symbols and graph queries** — `cdidx` indexes assembly labels, PROC/MACRO blocks, sections/segments, extern/include/import directives, constants, and direct call/branch targets so `symbols`, `outline`, `references`, `callers`, and `callees` work for `.s`, `.S`, `.asm`, and `.nasm` files.

## 日本語

- **Assembly ファイルがシンボル抽出と graph query に対応しました** — `.s`、`.S`、`.asm`、`.nasm` でラベル、PROC/MACRO、section/segment、extern/include/import、定数、直接 call/branch ターゲットを索引し、`symbols`、`outline`、`references`、`callers`、`callees` で扱えるようにしました。
