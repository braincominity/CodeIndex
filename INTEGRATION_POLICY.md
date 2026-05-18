# AI Agent, IDE, Editor, CI, and MCP Integration Policy

CodeIndex is designed to be called by humans and AI tools.

This policy explains which integrations are allowed without a separate written
agreement. It does not replace the license text in `LICENSE` or
`LICENSES/FSL-1.1-ALv2.txt`.

## Allowed Integrations

You may integrate with CodeIndex without a separate agreement when the integration
helps users use official CodeIndex releases for their own development work.

Allowed integrations include:

- AI coding agents invoking `cdidx` through CLI commands;
- AI coding agents invoking `cdidx mcp`;
- IDE/editor extensions that discover or call a user-installed `cdidx`;
- IDE/editor extensions that provide configuration UI for official CodeIndex;
- shell scripts, task runners, and CI jobs that run `cdidx`;
- MCP client configuration files;
- devcontainer, Dockerfile, and setup examples that install official CodeIndex
  releases;
- package-manager formulas, recipes, manifests, or mirrors that redistribute
  unmodified official CodeIndex releases with notices preserved;
- internal company wrappers that help employees use CodeIndex on authorized
  repositories;
- documentation showing how to integrate CodeIndex with AI assistants,
  terminals, editors, and CI systems.

## Allowed Data and Outputs

When you use CodeIndex on codebases you own, control, or are authorized to
access:

- your source code remains yours;
- your index files remain yours;
- search results, snippets, structured JSON, and MCP responses generated from
  your own codebases remain yours;
- CodeIndex does not claim ownership over your repositories or generated
  development context.

## API Surface and Library Use

CodeIndex ships primarily as a **CLI and MCP server**. It does not publish a
general-purpose library or SDK API for embedders.

- The stable, supported surfaces are the `cdidx` CLI (including its `--json`
  output) and the `cdidx mcp` JSON-RPC interface. Versioning guarantees only
  apply to those surfaces.
- The `cdidx` NuGet package is published with `PackAsTool=true` and is intended
  to be installed as a .NET global tool, not added as an assembly reference.
- Types that happen to be `public` on the `cdidx` assembly (for example DTOs in
  `CodeIndex.Database` / `CodeIndex.Models`, or readers such as
  `CodeIndex.Database.DbReader`) exist to satisfy CLI and MCP composition. They
  are **implementation details**, not a public library contract, and may
  change, move, or become `internal` in any release without a deprecation
  cycle.
- Projects that need a programmatic interface should depend on the CLI's
  `--json` output or on the MCP server, both of which are covered by the
  changelog and the documented status contract.
- The extractor plugin interfaces under `CodeIndex.Indexer.Extensibility` are
  a narrow exception for language-extension DLLs loaded by `cdidx` itself from
  `.cdidx/plugins` or `~/.cdidx/plugins`. They are not a general embedding API.
  Plugins run in the `cdidx` process and must be treated as trusted local code.

If a future need justifies a real library API, it will be carved out as a
separate package with its own explicit interface and versioning contract.
Until then, treat embedding the `cdidx` assembly outside the extractor plugin
contract as unsupported.

## CLI JSON and MCP Response Compatibility

The CLI `--json` surface and MCP tool responses are both stable integration
surfaces, but they are not byte-for-byte identical envelopes. CLI JSON is shaped
for command-line automation and preserves command-specific naming and output
wrappers. MCP responses are shaped for JSON-RPC tool calls and use camelCase
field names with MCP-specific tool metadata.

Integrations should parse the surface they call directly instead of assuming a
CLI payload can be substituted for an MCP payload without adaptation. Additive
fields may appear on either surface in minor releases; consumers should ignore
unknown fields and prefer documented fields over positional assumptions.

| Query surface | CLI `--json` shape | MCP response shape | Compatibility notes |
|---|---|---|---|
| `search` | One JSON object per result, with CLI query metadata such as `api_version`, `query`, path, line range, snippet, highlights, and truncation details. | Tool result content contains equivalent search-result objects using MCP serialization and tool-call framing. | Result semantics are shared, but the outer envelope and field casing follow the called surface. |
| `references` | Reference rows expose the raw indexed reference kind for each matching site and CLI-oriented row fields. | The `references` tool returns reference rows through MCP framing and may include graph-support metadata when a language filter is provided. | `references` is the raw-reference enumeration path. Use it for metadata kinds such as `attribute` / `annotation` rather than expecting `callers` to surface those rows. |
| `callers` | Grouped caller rows expose `reference_kind` as the preferred summary label, plus `reference_kinds` and `has_mixed_reference_kinds` in snake_case JSON. Human output prints the grouped kind label instead of the JSON fields. | Grouped rows expose `referenceKind`, sorted `referenceKinds`, and `hasMixedReferenceKinds` in camelCase. | The scalar summary field is retained for backward compatibility. Consumers that need all underlying kinds should read the array field for their surface; the mixed flag is `true` when one grouped row represents multiple distinct kinds, such as `call` + `subscribe`. |
| `callees` | Grouped callee rows expose the same snake_case mixed-kind fields as `callers`; callee rows generally stay split per kind after duplicate physical-site collapse. | Grouped rows expose the same `referenceKind`, `referenceKinds`, and `hasMixedReferenceKinds` contract as MCP `callers`. | Treat the array field as the authoritative set even when it has a single element, so clients use one parsing path for callers and callees. |

Version markers for these fields:

- `api_version` is the CLI JSON payload contract marker and currently remains
  `"1"` for the command surfaces that expose it.
- `reference_kind` is the CLI grouped-row summary field; MCP exposes the same
  concept as `referenceKind`.
- `reference_kinds` / `has_mixed_reference_kinds` are stable CLI grouped-row
  fields on `callers` and `callees`; MCP exposes the same concepts as
  `referenceKinds` / `hasMixedReferenceKinds` on `callers`, `callees`, and
  bundled `analyze_symbol` caller/callee rows.
- New fields documented in the changelog are additive unless a release note
  explicitly marks a breaking change.

## Boundary

The integration is allowed when it helps users operate official CodeIndex
releases or permissively licensed integration materials.

The integration may require a separate written agreement when it makes CodeIndex,
a modified CodeIndex engine, or a derivative work of CodeIndex available to
third parties as a commercial product or service that substitutes for CodeIndex
or offers substantially similar indexing/search/retrieval functionality.

## Examples

Allowed:

- "My agent calls `cdidx search` before editing files."
- "My agent calls `cdidx status --check --json` before searching so it can skip unnecessary reindexing when the DB already matches the workspace."
- "My VS Code extension lets users configure the path to their local `cdidx`."
- "My CI job runs `cdidx .` and `cdidx search` against our private repo."
- "My devcontainer installs the official `cdidx` release."
- "My internal platform lets employees search company repositories using
  CodeIndex."

Requires separate agreement:

- "I sell a hosted code search service powered by CodeIndex."
- "I publish a renamed CodeIndex fork as my own MCP code search product."
- "I embed a modified CodeIndex engine as the core retrieval feature of my
  commercial AI coding SaaS."
- "I offer a commercial IDE product where the CodeIndex-derived engine is a
  primary code-indexing/search feature offered to third parties."

## Trademarks

You may make truthful compatibility statements such as:

- "compatible with CodeIndex"
- "uses cdidx if installed"
- "configuration for CodeIndex"

You may not imply that your integration is official, endorsed, approved, or
maintained by Widthdom unless you have written permission. See `TRADEMARKS.md`.
