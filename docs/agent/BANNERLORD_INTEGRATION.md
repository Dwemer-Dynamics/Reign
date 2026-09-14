# Bannerlord integration boundary

The Bannerlord project should remain a thin adapter over server/domain contracts.

- Read TaleWorlds state and translate it into stable Reign DTOs.
- Call Reign through the existing integration clients.
- Apply returned commands through campaign behaviors and runtime patches.
- Keep calculations that can be deterministic and Bannerlord-independent in domain modules.
- Do not place PostgreSQL drivers, schema logic, or database credentials in the Bannerlord client.

Internal TaleWorlds-facing implementation changes normally use the owning module's Bannerlord facet at Tier 2. A client/server or adjacent-module contract change uses Tier 3 boundary validation. Shared DTOs, save serialization/schema, packaging, project references, and explicit release checks require Tier 4.
