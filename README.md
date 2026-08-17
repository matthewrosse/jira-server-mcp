# jira-server-mcp

An MCP server that lets coding agents work against a legacy, self-hosted **Jira Server** (8.14+,
Server or Data Center). Not Jira Cloud — a different API and a different auth model, and out of
scope.

- Speaks the Model Context Protocol over stdio.
- Authenticates with a **personal access token**, kept in the operating system's **credential
  store**.
- One **profile** — one named Jira Server — per process.
- Write tools are registered only where the operator handed the client the matching **grant**.

## Install

```
dotnet tool install --global jira-server-mcp
```

Self-contained binaries for `win-x64`, `win-arm64`, `osx-arm64`, `osx-x64`, `linux-x64` and
`linux-arm64` are attached to each GitHub release, with checksums, for machines with no .NET SDK.

## Get started

```
jira-server-mcp profile add work --url https://jira.example.com
jira-server-mcp auth login work
jira-server-mcp serve --profile work --allow comments:write
```

Full documentation, including the MCP client configuration to copy, is coming with the first
release.

## Licence

MIT. See [LICENSE](LICENSE).
