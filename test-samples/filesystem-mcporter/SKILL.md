---
name: filesystem-mcporter
description: Use the installed filesystem MCP server via mcporter when you need to inspect or work with files under the local Windows user profile.
---

# Filesystem MCP via mcporter

Use `mcporter` to access the installed filesystem MCP server named `filesystem-home`.

## Quick checks

- List configured MCP servers: `mcporter list`
- Show this server schema: `mcporter list filesystem-home --schema`

## Notes

- This server is scoped to the local Windows home directory.
- Prefer it when you need structured filesystem operations through MCP instead of ad-hoc shell commands.
- If the server is missing, re-register it with mcporter using the `filesystem-home` name.