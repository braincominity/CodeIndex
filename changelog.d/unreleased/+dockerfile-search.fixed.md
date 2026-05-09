---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/References/Languages/DockerfileReferenceExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Dockerfile stage aliases can include hyphens** — Dockerfile symbol and stage-reference extraction now indexes common aliases such as `build-env` in `FROM ... AS build-env`, `FROM build-env AS ...`, and `COPY --from=build-env`.

## 日本語

- **Dockerfile の stage alias でハイフンを扱えるようになりました** — Dockerfile の symbol 抽出と stage 参照抽出が、`FROM ... AS build-env`、`FROM build-env AS ...`、`COPY --from=build-env` のような一般的な alias を index するようになりました。
