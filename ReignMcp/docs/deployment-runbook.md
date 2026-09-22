# Reign MCP deployment

Deploy the separate STDIO MCP host from a successful canonical validation report. This does not replace the installed Reign server/client, migrate campaign data, start a listener or change provider settings.

## Prepare from validated artifacts

Check active same-project tasks and `reign_get_validation_status` before shared operations; use [task coordination](../../docs/agent/TASK_COORDINATION.md) for an overlap. Obtain the manifest-selected plan and successful `reign_validate` report. Validator changes require ecosystem coverage. Inspect its repository-hygiene result and exact source fingerprint.

Use `<artifactRoot>/mcp/out/Reign.Mcp.Server.dll` and its complete output directory from that report. Verify the file exists and record its SHA-256 with the report/run ID. Do not run another build or publish for deployment. The old `prepare-deployment.ps1` rebuild/package path is superseded for this workflow.

Finish source changes and validation before activation. A report for different or subsequently changed source is not acceptance evidence; ask the validator to determine compatible reuse or the required new run.

## Activate

Back up the exact current project `.codex/config.toml` into the task's ignored evidence directory. In the existing `[mcp_servers.reign]` table, replace only the DLL argument with the absolute path to the validated artifact. Preserve all other MCP registrations, environment values, approval modes and timeouts. Record the before/after configuration hashes and artifact hash in a deployment receipt.

For a new registration, derive only the Reign table and its existing gates from `deployment/config.toml.template`, substituting the same validated DLL and workspace paths. The legacy `activate-project.ps1` intentionally refuses an existing configuration and expects its older package manifest; do not use it to overwrite or rebuild an existing deployment.

Keep the validated output directory intact while configured. Restart/reconnect the MCP client to load the new host. When Codex itself must restart, save a task checkpoint and use the installed `restart_codex_and_resume_task` with `CODEX_THREAD_ID`, following [continuity procedures](../../docs/agent/CONTINUITY.md). Do not substitute process killing or restart the visible Reign server for an MCP-only update.

## Read-only acceptance

Validate the exact output over STDIO: initialize it, discover tools/resources/prompts, call `reign_get_validation_status`, inspect the plan/catalog and read the testing-guide resource. Compare capabilities with the current catalog and contract tests rather than a frozen tool count. Confirm status is read-only, `requestingTaskId` is exposed on validation, and mutation gates remain intact.

The existing `McpHostIntegrationTests` exercises STDIO discovery and status during canonical validation; `REIGN_MCP_TEST_SERVER_DLL` can bind it to a packaged DLL when that additional check is needed. For activation, retain a separate receipt from the configured exact host. Runtime parity checks may read current status when relevant, but must not start verification, change providers or mutate a campaign.

A successful status response proves the deployed host is callable, not that another validation completed. Use the canonical report for build/test evidence. Report the deployed DLL/hash, validation and hygiene paths, and acceptance receipt.

## Rollback

Restore the backed-up Reign DLL argument after confirming it is still the only configuration delta; preserve any later unrelated changes. Reconnect the MCP client through the same supported route. The previous validated artifact remains inert until selected. To disable the integration, set only `mcp_servers.reign.enabled = false` and reconnect; no installed Reign files or campaign state need rollback.
