# Manual test plan

1. Build and run the automated tests while Reign remains untouched.
2. Add the MCP configuration only after the current live test is stopped or at a natural checkpoint.
3. Connect with every mutation tool approval-gated.
4. List tools, resources, resource templates, and prompts.
5. Call `reign_get_capabilities` and confirm the expected workspace and `127.0.0.1:5101`.
6. With Reign stopped, verify `reign_get_status` fails cleanly without starting it.
7. Start Reign through its supported visible launcher, then verify status and campaign discovery.
8. Compare one `reign_get_world_overview` and subsystem drill-down against the Control Center.
9. Query one world event, audit correlation, action-failure set, and verification result.
10. Confirm source tools refuse absolute paths, `..`, runtime `data`, logs, staging, binaries, and reparse points.
11. Call `reign_get_validation_plan` with `product`; verify editor, native portrait, and importer projects are absent. Then plan `all` and verify every active project is classified while copied/generated/decompiled projects remain absent.
12. Call `reign_validate` with `tooling`; verify builds, test execution, TRX files, and the structured report remain under `.codex-build/reign-mcp`.
13. Run the isolated core quick tier; verify it completes the bounded smoke set, starts no listener, and does not change real campaign directories.
14. Review and trust the project hook through `/hooks`, then confirm a direct `dotnet build` is denied with an MCP routing message.
15. Exercise live verification control only after no other run is active and verify both approval and exact confirmation.
16. Do not deploy or remove existing World Test components during this validation pass.
