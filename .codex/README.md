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

## No-SDK cloud bootstrap

For SDK-less Codex cloud sessions, use `CLOUD_BOOTSTRAP_PROMPT.md`. The guard
permits only the official CodeIndex installer bootstrap forms: the exact
`raw.githubusercontent.com/Widthdom/CodeIndex/.../install.sh | bash` one-liner
and direct repo-local `bash ./install.sh ...` commands with supported installer
flags, plus the exact resolver-print command and MCP initialize smoke command
documented in that prompt. Arbitrary downloads and bare/global `cdidx` remain
blocked.

After installation, invoke `cdidx` through the fully expanded absolute path
documented in `CLOUD_BOOTSTRAP_PROMPT.md`; do not use bare `cdidx` or
`$HOME/.local/bin/cdidx`, and do not use `$CDIDX` / `${CDIDX}` because the guard
cannot validate variable contents.
