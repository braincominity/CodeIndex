---
category: fixed
affected:
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - src/CodeIndex/Database/DbReader.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Java search now accepts a shorthand `jav` language filter** — query commands normalize `jav` to `java` and advertise it in language help/completion, so Java searches work with the same style of shorthand already available for other major languages.

## 日本語

- **Java 検索で `jav` という省略形の言語フィルタを使えるようになりました** — クエリ系コマンドは `jav` を `java` に正規化し、言語ヘルプ / 補完にも表示するため、他の主要言語と同じ感覚で Java 検索を省略形でも実行できます。
