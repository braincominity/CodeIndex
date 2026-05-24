# cdidx

`cdidx` is a .NET global tool for local code indexing, CLI search, and MCP
workflows. It builds a local SQLite index so humans and AI agents can query a
repository without repeatedly rescanning the same tree.

## Install or update

```bash
dotnet tool install -g cdidx
dotnet tool update -g cdidx
```

## Quick start

```bash
cdidx .
cdidx status --check --json
cdidx search "handleRequest"
cdidx definition UserService
cdidx mcp
```

## Documentation

NuGet renders this README outside the GitHub repository, so these links use
absolute GitHub URLs.

| Document | Link |
|---|---|
| README | https://github.com/Widthdom/CodeIndex/blob/main/README.md |
| User Guide | https://github.com/Widthdom/CodeIndex/blob/main/USER_GUIDE.md |
| Platform Support | https://github.com/Widthdom/CodeIndex/blob/main/docs/platform-support.md |
| Developer Guide | https://github.com/Widthdom/CodeIndex/blob/main/DEVELOPER_GUIDE.md |
| Integration Policy | https://github.com/Widthdom/CodeIndex/blob/main/INTEGRATION_POLICY.md |
| Security Policy | https://github.com/Widthdom/CodeIndex/blob/main/SECURITY.md |
| Changelog | https://github.com/Widthdom/CodeIndex/blob/main/CHANGELOG.md |

`cdidx` is distributed as a CLI and MCP server only. The NuGet package is a
global tool package and does not provide a public library or SDK API.
