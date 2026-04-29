# Codex Setup

This repository includes a project-local Codex config and hook policy.

## Enable and trust

Open the repository in Codex and trust the project when prompted so Codex loads
`.codex/config.toml` and `.codex/hooks.json`.

Keep the config repository-local. Do not add machine-specific home directory
paths to committed config files.

## Safe usage

For CodeIndex work, dogfood the repo-built binary for code search:

```bash
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll search SymbolExtractor
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll symbols --lang csharp
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll inspect src/CodeIndex/Indexer/SymbolExtractor.cs
```

The guard hooks block grep-like search escape hatches, global `cdidx`, and
high-risk commands so Codex stays inside the repository policy.
