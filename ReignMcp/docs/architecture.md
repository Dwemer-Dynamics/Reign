# Architecture

## Purpose

Reign MCP gives AI development clients one discoverable, typed interface for Reign diagnostics, campaign observability, verification, and isolated builds. It is not part of gameplay authority.

```text
MCP client
   |
   | MCP over STDIO
   v
Reign.Mcp.Server (.NET 10, optional development process)
   |                         |
   | fixed loopback HTTP     | confined source/build access
   v                         v
ReignBetaServer :5101        Bannerlord Events workspace
   |
   v
Campaign SQLite / bounded logs / Verification Lab
```

The host is separate from both the .NET Framework Reign server and Bannerlord. MCP SDK dependencies never enter either release runtime.

## Runtime profiles and future extraction

The intended long-term split is:

| Profile | Responsibility |
| --- | --- |
| Release game | Minimal bounded native telemetry and authoritative execution only |
| Diagnostic game | Release behavior plus explicitly armed disposable-save test hooks |
| Reign server | Durable campaign state, API contracts, validation, and telemetry aggregation |
| MCP host | World Test presentation and analysis, developer diagnostics, source inspection, builds, and test coordination |

World Test's web presentation and diagnostic workflows can move outward into MCP clients. A small game-side telemetry producer must remain for facts only Bannerlord can authoritatively observe: native identities, campaign time, native outcomes, action receipts, queue progress, and live subsystem counters.

Removing every native observation hook would make an external MCP server blind. The optimization target is therefore test UI/controllers and redundant work, not authoritative telemetry.

## Authority

- LLM and MCP clients may inspect, compare, and recommend.
- Reign server validation remains authoritative for server state.
- Bannerlord-side C# remains authoritative for native game state and actions.
- An offline MCP or Verification Lab result never proves native game behavior.
- MCP never deploys or controls the supported Reign server/Control Center/vector-worker lifetime group.

## Evolution

The API client is deliberately route-specific rather than a raw proxy. New Reign areas should receive a purpose-built tool with:

1. a stable Reign API contract;
2. bounded inputs and output;
3. deterministic redaction;
4. accurate MCP risk annotations;
5. code-enforced authorization/gating;
6. isolated tests;
7. an explicit native-authority and acceptance statement.

