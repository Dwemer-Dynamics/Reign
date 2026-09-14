# Deterministic Reign scenario fixtures

Scenario snapshots in this directory are small, versioned inputs for Bannerlord-independent domain tests. A fixture must contain only the state required by the owning subsystem, use stable IDs and timestamps, declare a `schemaVersion`, and state its expected invariants in its test.

Planned fixture families include `kingdom_at_war`, `court_intrigue`, `relationship_party_20`, `ruler_absent_30_days`, `rumor_propagation`, and `diplomacy_alliance`. Add a fixture with the first extracted pure behavior that consumes it; do not copy full campaign databases here.
