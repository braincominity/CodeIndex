# cdidx

`cdidx` is a .NET global tool for local code indexing, CLI search, MCP
workflows, and read-only LSP editor lookup. It builds a local SQLite index so
humans, AI agents, and editors can query a repository without repeatedly
rescanning the same tree.

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
cdidx lsp --db .cdidx/codeindex.db
```

## Documentation

NuGet renders this README outside the GitHub repository, so these links use
absolute GitHub URLs.

| Document | Link |
|---|---|
| README | https://github.com/Widthdom/CodeIndex/blob/{{RELEASE_REF}}/README.md |
| User Guide | https://github.com/Widthdom/CodeIndex/blob/{{RELEASE_REF}}/USER_GUIDE.md |
| Platform Support | https://github.com/Widthdom/CodeIndex/blob/{{RELEASE_REF}}/docs/platform-support.md |
| Developer Guide | https://github.com/Widthdom/CodeIndex/blob/{{RELEASE_REF}}/DEVELOPER_GUIDE.md |
| Integration Policy | https://github.com/Widthdom/CodeIndex/blob/{{RELEASE_REF}}/INTEGRATION_POLICY.md |
| Security Policy | https://github.com/Widthdom/CodeIndex/blob/{{RELEASE_REF}}/SECURITY.md |
| Changelog | https://github.com/Widthdom/CodeIndex/blob/{{RELEASE_REF}}/CHANGELOG.md |

`cdidx` is distributed as a CLI, MCP server, and LSP shim only. The NuGet
package is a global tool package and does not provide a public library or SDK
API.
