# Reign Workspace Source-Control Implementation Plan

> **Historical plan, superseded 2026-09-14.** Its repository-creation and publishing instructions are retired. Current development uses the paired `Dwemer-Dynamics/Reign` and `Dwemer-Dynamics/ReignServer` repositories. Follow [repository recovery](../../agent/REPOSITORY_RECOVERY.md) and [launch cutover](../../agent/REIGN_LAUNCH_IMPLEMENTATION.md). The original plan below is retained as history.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish the complete, safely classified Reign workspace to a new private `speedaemonc4/Reign-Workspace` GitHub repository and make repository hygiene plus durable project memory enforceable for future Codex tasks.

**Architecture:** Keep the current local Git history, add a root repository-policy manifest and exclusions, and integrate a deterministic repository-hygiene audit into the existing Reign MCP validation report. Stage only explicitly classified source and documentation, run the manifest-selected ecosystem Tier 4/offline validation, then create and verify the private GitHub remote and a fresh clone. Preserve the two `Dwemer-Dynamics` repositories unchanged.

**Tech Stack:** Git, GitHub CLI, C#/.NET 10 Reign MCP, xUnit, JSON policy manifests, Markdown, PowerShell launch scripts, Reign MCP validation.

## Global Constraints

- The new repository is exactly `speedaemonc4/Reign-Workspace` and must remain private.
- Do not modify, rename, archive, transfer, force-push, or delete `Dwemer-Dynamics/Reign` or `Dwemer-Dynamics/Reign-Server`.
- Preserve the existing local commit history and every unrelated user worktree change.
- Never use `git add .`, `git add -A`, a broad wildcard, or a recursive staging command before repository exclusions and classification are proven.
- Never track saves, Save Sync data, runtime databases, API credentials, logs, generated output, deployment backups, decompiled game/dependency sources, third-party binaries, or local caches.
- Never print a discovered secret value. Evidence may contain only the path and classifier identifier.
- The ordinary tracked-file ceiling is 25 MiB (`26214400` bytes); larger files require an explicit allowlist entry and review.
- Reign builds and tests run only through `reign_get_validation_plan` and `reign_validate`; do not invoke `dotnet`, MSBuild, VSTest, or project scripts directly.
- The already selected validation route for validator and manifest changes is `changed` resolving to ecosystem Tier 4 with complete offline verification.
- Leave both visible Reign windows and all save/campaign state untouched; this work needs no server or Bannerlord lifecycle operations.

---

## File structure

**Create:**

- `.gitignore` — workspace-wide generated/private/runtime exclusions.
- `.gitattributes` — deterministic text normalization and binary-asset classification.
- `reign.repository.json` — machine-readable repository-hygiene policy.
- `docs/agent/PROJECT_MEMORY.md` — index for durable project memory.
- `docs/agent/DECISIONS.md` — dated architectural and product decisions.
- `docs/agent/DEBUGGING_HISTORY.md` — costly failures, causes, attempts, fixes, and evidence.
- `docs/agent/PERFORMANCE.md` — measured performance decisions and evidence.
- `docs/agent/KNOWN_PITFALLS.md` — compact recurring hazards.
- `docs/agent/REPOSITORY_RECOVERY.md` — clone, recovery, and owner checklist.
- `ReignMcp/src/Reign.Mcp.Server/Modules/Validation/ReignRepositoryHygieneAudit.cs` — deterministic Git-backed classifier and structured evidence.
- `ReignMcp/tests/Reign.Mcp.Tests/Validation/ReignRepositoryHygieneAuditTests.cs` — isolated hygiene contract tests.

**Modify:**

- `AGENTS.md` — canonical-remote, tracking, memory, and external-write rules.
- `reign.modules.json` — ownership/routing for root repository policy files.
- `reign.testing.json` — repository-hygiene evidence contract and version.
- `docs/agent/TESTING_TOOL_GUIDE.md` — operating and interpreting the hygiene gate.
- `ReignMcp/src/Reign.Mcp.Server/Modules/Platform/Program.cs` — register the audit service.
- `ReignMcp/src/Reign.Mcp.Server/Modules/Validation/Models.cs` — add structured hygiene report models to validation evidence.
- `ReignMcp/src/Reign.Mcp.Server/Modules/Validation/ReignValidationService.cs` — execute and persist the hygiene audit.
- `ReignMcp/tests/Reign.Mcp.Tests/Validation/ReignValidationServiceTests.cs` — validate report gating and audit-mode behavior.
- `ReignMcp/tests/Reign.Mcp.Tests/Validation/ReignProjectCatalogTests.cs` — validate routing of root policy files.
- `ReignMcp/tests/Reign.Mcp.Tests/Platform/TestingCatalogContractTests.cs` — prevent catalog/guide/evidence drift.

---

### Task 1: Define repository policy and failing contracts

**Files:**

- Create: `.gitignore`
- Create: `.gitattributes`
- Create: `reign.repository.json`
- Create: `ReignMcp/tests/Reign.Mcp.Tests/Validation/ReignRepositoryHygieneAuditTests.cs`
- Modify: `reign.modules.json`
- Modify: `ReignMcp/tests/Reign.Mcp.Tests/Validation/ReignProjectCatalogTests.cs`

**Interfaces:**

- Produces: `reign-repository-policy-v1` with `enforcement`, `canonicalRemote`, `requiredTrackedPaths`, `managedSourceRoots`, `prohibitedTrackedPatterns`, `humanAuthoredExtensions`, `secretNamePatterns`, `secretContentClassifiers`, `maxOrdinaryFileBytes`, and `largeFileAllowlist`.
- Produces: expected `ReignRepositoryHygieneAudit.AuditAsync(CancellationToken)` behavior for Task 2.

- [ ] **Step 1: Add the root exclusion policy before any broad Git inventory**

Use this root `.gitignore` baseline, retaining narrower nested `.gitignore` files:

```gitignore
# Codex, verification, and transient workspace output
/.codex-build/
/.codex-live-artifacts/
/.codex-live-backups/
/.tmp/
/tmp/
/artifacts/
/logs/

# Legacy, decompiled, inspection, and promotional reference trees
/AIInfluence_decompiled/
/AIInfluence_inspect/
/Bannerlord_CampaignSystem_decompiled/
/BannerlordReign/
/Discord Promotions/
/PortraitCache/

# Runtime installation, deployment, campaign, and backup state
/ReignBeta/staging/
/ReignBeta/server/
/ReignBetaServer/app/
/ReignBetaServer/data/
/ReignBetaServer/logs/
**/deployment-backups/
**/verification_contracts/
**/staging/

# Build and package output
**/bin/
**/obj/
**/.vs/
**/TestResults/
**/build/
**/dist/
**/publish/
**/.build-venv/
**/.venv/
**/__pycache__/
**/node_modules/
*.user
*.suo
*.pdb
*.cache

# Runtime data and secrets
.env
.env.*
!.env.example
*.db
*.sqlite
*.sqlite3
*.sav
*.pfx
*.pem
*.key
*credentials*.json
*secrets*.json

# Logs, command captures, and temporary files
*.log
*.tmp
*.bak
/reignbeta_*_stdout.txt
/reignbeta_*_stderr.txt
/test-results-*.txt

# Generated root packaging artifact containing a machine-local absolute path
/ReignVectorWorker.spec
```

- [ ] **Step 2: Add deterministic attributes**

Create `.gitattributes` with LF normalization for authored text, explicit CRLF for Windows launchers, and binary treatment for media:

```gitattributes
* text=auto
*.cs text eol=lf
*.csproj text eol=lf
*.props text eol=lf
*.targets text eol=lf
*.json text eol=lf
*.md text eol=lf
*.ps1 text eol=crlf
*.cmd text eol=crlf
*.xml text eol=lf
*.yml text eol=lf
*.yaml text eol=lf
*.toml text eol=lf
*.py text eol=lf
*.png binary
*.jpg binary
*.jpeg binary
*.webp binary
*.gif binary
*.woff binary
*.woff2 binary
```

- [ ] **Step 3: Create the machine-readable policy in audit mode**

Start `reign.repository.json` with `"enforcement": "audit"` so the new checker can report the pre-baseline gaps without blocking the first implementation test. Use exactly `26214400` for `maxOrdinaryFileBytes`, the canonical HTTPS remote `https://github.com/speedaemonc4/Reign-Workspace.git`, and required paths for `AGENTS.md`, all three Reign manifests, `.gitignore`, `.gitattributes`, and every project-memory file.

The managed source roots are:

```json
[
  ".codex",
  "BannerlordEditorMcp",
  "NativeCharacterImageGenerator",
  "ReignBeta",
  "ReignBetaServer",
  "ReignMcp",
  "ReignModules",
  "ReignTools",
  "docs",
  "tests",
  "tools"
]
```

The policy must classify, without reading protected runtime contents, the same excluded roots as `.gitignore`; human-authored extensions include `.cs`, `.csproj`, `.props`, `.targets`, `.json`, `.md`, `.ps1`, `.cmd`, `.xml`, `.yml`, `.yaml`, `.toml`, `.py`, `.spec`, `.sln`, and `.slnx`. Secret-content classifiers must be identifiers with redacted matching, including `private-key-header`, `github-token`, `openai-api-key`, `anthropic-api-key`, `generic-api-key-assignment`, and `password-assignment`.

- [ ] **Step 4: Write failing audit tests**

Use isolated temporary Git fixtures and a fake Git client. Cover at least these exact cases:

```csharp
[Fact] public async Task EnforceModeRejectsUntrackedHumanAuthoredSource();
[Fact] public async Task EnforceModeRejectsTrackedRuntimeOrDecompilerPath();
[Fact] public async Task EnforceModeRejectsMissingRequiredTrackedPath();
[Fact] public async Task EnforceModeRejectsOversizedFileWithoutAllowlist();
[Fact] public async Task EnforceModeReportsSecretClassifierWithoutSecretValue();
[Fact] public async Task AuditModeReportsIssuesWithoutFailingValidation();
[Fact] public async Task CleanClassifiedRepositoryPasses();
```

Assert category IDs, normalized workspace-relative paths, `Ok`, enforcement mode, and issue counts. Assert that a sentinel secret value never appears in serialized evidence.

- [ ] **Step 5: Route policy files in `reign.modules.json`**

Add `.gitignore`, `.gitattributes`, and `reign.repository.json` as explicit Tier 4 repository/validator infrastructure paths. Keep project-memory Markdown under the existing `**/docs/**` non-code rule. Add catalog tests proving these exact paths resolve intentionally and that `reign.repository.json` selects ecosystem Tier 4/offline validation.

- [ ] **Step 6: Confirm the tests fail for the missing implementation**

Call `reign_get_validation_plan` with the exact Task 1 paths, then call `reign_validate(profile="changed", configuration="Release", restore=false)` with those paths. Expected result: failure because `ReignRepositoryHygieneAudit` and its result contracts do not yet exist—not an unrelated catalog or workspace-coverage failure.

---

### Task 2: Implement and integrate the repository-hygiene audit

**Files:**

- Create: `ReignMcp/src/Reign.Mcp.Server/Modules/Validation/ReignRepositoryHygieneAudit.cs`
- Modify: `ReignMcp/src/Reign.Mcp.Server/Modules/Platform/Program.cs`
- Modify: `ReignMcp/src/Reign.Mcp.Server/Modules/Validation/Models.cs`
- Modify: `ReignMcp/src/Reign.Mcp.Server/Modules/Validation/ReignValidationService.cs`
- Modify: `ReignMcp/tests/Reign.Mcp.Tests/Validation/ReignValidationServiceTests.cs`
- Test: `ReignMcp/tests/Reign.Mcp.Tests/Validation/ReignRepositoryHygieneAuditTests.cs`

**Interfaces:**

- Produces: `Task<RepositoryHygieneReport> AuditAsync(CancellationToken cancellationToken)`.
- Produces: `RepositoryHygieneReport` with `Schema`, `Enforcement`, `Ok`, `TrackedFileCount`, `UntrackedHumanAuthoredPaths`, `Issues`, `PolicyPath`, and `EvidencePath`.
- Consumes: `reign.repository.json`, the workspace root, and bounded null-delimited Git output.

- [ ] **Step 1: Implement fail-closed policy parsing**

Reject a missing policy, schemas other than `reign-repository-policy-v1`, enforcement values other than `audit` or `enforce`, an empty canonical remote, unsafe absolute/glob traversal, duplicate normalized rules, non-positive file thresholds, and required paths outside the workspace.

- [ ] **Step 2: Implement bounded Git inventory**

Run only these read-only Git operations from the configured workspace root:

```text
git rev-parse --show-toplevel
git ls-files -z
git status --porcelain=v1 -z --untracked-files=all
git check-ignore -z --stdin
```

Require the resolved Git root to equal `options.WorkspaceRoot`. Parse null-delimited output, normalize `/` separators, cap output and issue counts, and return a structured `git-unavailable` or `wrong-repository-root` issue instead of falling back to an arbitrary directory scan.

- [ ] **Step 3: Classify tracked, untracked, ignored, oversized, and secret-risk files**

For tracked files, reject prohibited paths and files above 25 MiB unless allowlisted. For untracked files inside managed roots, report human-authored extensions. Use path/name checks before content checks. Scan only tracked, allowlisted text extensions below the size cap for secret signatures; never scan ignored runtime databases, saves, logs, or deployment trees. Report the classifier ID and path only.

- [ ] **Step 4: Integrate structured evidence into validation**

Register `ReignRepositoryHygieneAudit` in `Program.cs` and inject it into `ReignValidationService`. Run the audit after the validation run directory is created and before project builds. Persist `repository-hygiene-report.json` inside the validation run root. Add the report to `ValidationReport`, increment the validation schema/version, and include `hygiene.Ok` in overall `Ok` only when policy enforcement is `enforce`. In `audit` mode, preserve issues as evidence but do not block the implementation bootstrap.

- [ ] **Step 5: Extend validation service contracts**

Add tests proving:

```csharp
[Fact] public async Task ValidateAsync_EnforceModeStopsBeforeBuildWhenHygieneFails();
[Fact] public async Task ValidateAsync_AuditModePersistsIssuesAndContinues();
[Fact] public async Task ValidateAsync_CleanRepositoryPersistsPassingHygieneEvidence();
```

Use test-owned temporary workspaces and fake process responses. Assert that a hygiene failure cannot be hidden by successful project results or report reuse.

- [ ] **Step 6: Run the smallest manifest-selected implementation validation**

Call `reign_validate` with every Task 1 and Task 2 changed path. Expected: ecosystem Tier 4/offline is selected; the new unit contracts pass; repository hygiene is present in the structured report in `audit` mode. Diagnose any failure and rerun only the smallest applicable manifest-selected profile.

- [ ] **Step 7: Commit the green validator implementation**

Stage only Task 1 and Task 2 files and commit:

```text
test: enforce Reign repository hygiene
```

---

### Task 3: Add durable memory and future-agent policy

**Files:**

- Create: `docs/agent/PROJECT_MEMORY.md`
- Create: `docs/agent/DECISIONS.md`
- Create: `docs/agent/DEBUGGING_HISTORY.md`
- Create: `docs/agent/PERFORMANCE.md`
- Create: `docs/agent/KNOWN_PITFALLS.md`
- Create: `docs/agent/REPOSITORY_RECOVERY.md`
- Modify: `AGENTS.md`
- Modify: `docs/agent/TESTING_TOOL_GUIDE.md`
- Modify: `reign.testing.json`
- Modify: `ReignMcp/tests/Reign.Mcp.Tests/Platform/TestingCatalogContractTests.cs`

**Interfaces:**

- Produces: a concise memory index and one stable format per durable knowledge category.
- Produces: agent rules that make tracking and memory updates part of future task handoff.

- [ ] **Step 1: Create focused memory documents**

Each document starts with purpose, inclusion criteria, and a dated-entry format. `PROJECT_MEMORY.md` links the existing architecture/current-system/database/dependency/testing/validation documents and the four new focused ledgers. Initial ledgers state “No durable entries recorded yet” rather than inventing history.

Use these entry fields:

```text
Date
Area
Context or symptoms
Decision or root cause
Alternatives/failed approaches (when applicable)
Consequences or successful fix
Verification/evidence
Follow-up
```

- [ ] **Step 2: Add future-agent repository rules to `AGENTS.md`**

Add the approved canonical-remote, inspect-before-edit, track-human-authored-work, protected-exclusion, durable-memory update, intentional-staging, handoff-reporting, and external-write authorization rules. State that the hygiene report from `reign_validate` is authoritative evidence and that passing compilation cannot override a failed hygiene gate.

- [ ] **Step 3: Document owner recovery and minimal maintenance**

`REPOSITORY_RECOVERY.md` must explain how to confirm private visibility, clone, locate `AGENTS.md`, restore non-versioned dependencies without copying saves/runtime data, run the Reign validation entrypoint, interpret hygiene failures, and transfer ownership later without changing local history.

- [ ] **Step 4: Update the testing catalog and guide**

Increment `reign.testing.json` from `2026.08.15.9`, add the repository-hygiene report schema/path, enforcement modes, 25 MiB threshold, redaction guarantee, and completion evidence. Update `TESTING_TOOL_GUIDE.md` with discovery, audit-to-enforce bootstrap, failure categories, and the rule that protected runtime paths are classified by path rather than inspected.

- [ ] **Step 5: Extend drift coverage**

Update `TestingCatalogContractTests` to require `reign-repository-policy-v1`, `repository-hygiene-report`, `26214400`, `audit`, `enforce`, and the redaction statement in the catalog/guide and validation source.

- [ ] **Step 6: Commit documentation and catalog policy**

After contract coverage passes through the manifest-selected validation route, stage only Task 3 files and commit:

```text
docs: add durable Reign project memory
```

---

### Task 4: Classify and stage the safe monorepo baseline

**Files:**

- Inspect: every path remaining after `.gitignore`
- Stage: explicitly accepted source/documentation roots and root contracts
- Preserve unstaged: every excluded or ambiguous path

**Interfaces:**

- Consumes: `reign.repository.json` in `audit` mode.
- Produces: a reviewable Git index containing the recoverable monorepo and no prohibited material.

- [ ] **Step 1: Record the pre-baseline checkpoint**

Record current branch, HEAD, tracked changes, untracked counts, both legacy repository HEADs, and GitHub visibility in `.codex-build/reign-mcp/repository-bootstrap/<run-id>/preflight.json`. Record the two pre-existing tracked deltas explicitly:

```text
ReignMcp/tests/Reign.Mcp.Tests/Reign.Mcp.Tests.csproj
ReignMcp/tests/Reign.Mcp.Tests/ReignCampaignReadinessStateTests.cs
```

Inspect their diff and include their current state only if the manifest-selected validation proves it coherent. Do not silently restore or discard either change.

- [ ] **Step 2: Run the audit-mode inventory**

Run `reign_validate` with the coherent implementation path set and inspect `repository-hygiene-report.json`. Resolve every issue by adding an exclusion, intentionally accepting the source path, or documenting why the file stays untracked. Never resolve an issue by weakening a protected category.

- [ ] **Step 3: Stage explicit recovery-critical roots**

Use path-explicit `git add --` calls for:

```text
.codex
.gitattributes
.gitignore
AGENTS.md
BannerlordEditorMcp
NativeCharacterImageGenerator
REIGN_ROADMAP.md
ReignBeta
ReignBetaServer
ReignMcp
ReignModules
ReignTools
docs
reign-projects.json
reign.modules.json
reign.repository.json
reign.testing.json
ReignCastleChatCore.cs
ReignKingdomEventCore.cs
ReignPortraitDerivativeCore.cs
ReignSpymasterCore.cs
tests
tools
```

Do not stage the ignored legacy/decompiled roots, `Discord Promotions`, `PortraitCache`, root `ReignVectorWorker.spec`, logs, test result text, runtime installations, deployment backups, or generated artifacts.

- [ ] **Step 4: Audit the staged tree**

Inspect `git diff --cached --name-status`, staged file count, staged byte total, largest staged files, file extensions, and prohibited-name matches. Search staged text blobs for secret classifiers without printing matched values. Confirm no staged path exceeds 25 MiB unless present in the policy allowlist.

- [ ] **Step 5: Switch the policy to enforce mode**

Change only `reign.repository.json` from `"audit"` to `"enforce"`, stage it, and rerun the repository audit. Expected: `Ok=true`, zero blocking issues, required paths tracked in the index, and protected/excluded material absent.

---

### Task 5: Validate and commit the complete baseline

**Files:**

- Validate: every changed/staged path selected by the implementation and baseline.
- Evidence: `.codex-build/reign-mcp/validation/<run-id>/validation-report.json`
- Evidence: `.codex-build/reign-mcp/validation/<run-id>/repository-hygiene-report.json`

**Interfaces:**

- Produces: an authoritative successful ecosystem Tier 4/offline validation report and source fingerprint.

- [ ] **Step 1: Obtain the final validation plan**

Call `reign_get_validation_plan(profile="changed", configuration="Release", restore=false)` with the exact changed path list from the index. Expected: ready, complete coverage, no unmanaged projects, ecosystem Tier 4, offline verification.

- [ ] **Step 2: Run canonical final validation**

Call `reign_validate` with the exact same changed paths. Do not start Reign or Bannerlord. Expected: all selected builds/tests and offline verification pass, repository hygiene passes in enforce mode, and both structured report paths exist.

- [ ] **Step 3: Commit the classified workspace**

Reconfirm the staged inventory has not changed since validation, then commit:

```text
chore: establish recoverable Reign workspace baseline
```

Record the commit SHA, source fingerprint, validation duration, validation profile/tier, and report paths in the bootstrap evidence directory.

- [ ] **Step 4: Create the canonical local branch without deleting history**

Verify `refs/heads/main` does not already exist, create `main` at the validated baseline commit, and switch to it. Preserve `master` and `codex/passive-world-observatory` as local historical branches.

---

### Task 6: Create and publish the private GitHub repository

**Files:**

- External create: `https://github.com/speedaemonc4/Reign-Workspace`
- Modify local Git config: add `origin`

**Interfaces:**

- Consumes: the validated `main` commit.
- Produces: a private GitHub repository with `main` as its default/upstream branch.

- [ ] **Step 1: Reconfirm authorization and destination state**

Verify GitHub CLI remains authenticated as `speedaemonc4`, `speedaemonc4/Reign-Workspace` does not already exist, and the two `Dwemer-Dynamics` repository HEADs still match the preflight checkpoint.

- [ ] **Step 2: Create an empty private repository**

Run:

```powershell
gh repo create speedaemonc4/Reign-Workspace --private --description "Private authoritative source and project memory for the Bannerlord Reign workspace."
```

Do not generate a README, license, `.gitignore`, or starter commit on GitHub.

- [ ] **Step 3: Configure and verify `origin`**

Add exactly:

```text
https://github.com/speedaemonc4/Reign-Workspace.git
```

Fetch the empty remote, confirm no unexpected refs, then push validated local `main` with upstream tracking. Never use `--force`.

- [ ] **Step 4: Verify remote state**

Use GitHub metadata and Git object checks to confirm private visibility, owner/name, default branch, remote HEAD SHA equal to the validated local SHA, expected tracked-file count, and absence of protected roots. Confirm both legacy organization repository HEADs remain unchanged.

---

### Task 7: Prove fresh-clone recovery and hand off owner guidance

**Files:**

- Create evidence: `.codex-build/reign-mcp/repository-bootstrap/<run-id>/fresh-clone-report.json`
- Verify: temporary clone outside the workspace

**Interfaces:**

- Produces: a bounded recovery report proving that GitHub can recreate the authored workspace without copying local runtime/private data.

- [ ] **Step 1: Clone into a validated temporary directory**

Create a task-specific temporary directory with `New-Item`, resolve its absolute path, confirm it is outside the source workspace, and clone `speedaemonc4/Reign-Workspace` into it.

- [ ] **Step 2: Verify the clone**

Run read-only Git integrity checks and assert:

```text
HEAD equals the validated baseline SHA
working tree is clean
AGENTS.md exists
reign-projects.json exists
reign.modules.json exists
reign.repository.json exists
reign.testing.json exists
all managed source roots exist
all project-memory documents exist
ReignMcp/scripts/reign-validate.ps1 exists
no prohibited root or runtime path is tracked
no tracked file exceeds the policy threshold without allowlisting
```

- [ ] **Step 3: Record recovery limitations honestly**

State that source recovery is proven. Do not claim a clean-machine product build unless legally redistributable Bannerlord/dependency prerequisites are also available and the canonical Reign validation actually runs in that clone. Record those external prerequisites in `REPOSITORY_RECOVERY.md` instead of uploading installed game binaries.

- [ ] **Step 4: Deliver the minimal owner checklist**

Tell the owner to:

1. Keep the repository private unless they intentionally change it.
2. Keep GitHub access for `speedaemonc4` active; reauthenticate only when GitHub reports an expired login.
3. Keep saves, keys, runtime databases, logs, generated portraits, build output, backups, and decompiled sources in their existing excluded locations.
4. Ask Codex to “document what we learned in Reign project memory” after a costly bug, architectural decision, or measured performance change.
5. Let Codex run the repository-hygiene and Reign validation checks before important pushes.
6. Transfer the repository later only through an explicit task; update `reign.repository.json`, `AGENTS.md`, local `origin`, and recovery documentation together.

- [ ] **Step 5: Final evidence handoff**

Report the private repository URL, branch, commit SHA, tracked-file count, excluded categories, validation profile, Tier 4/offline duration, source fingerprint, validation report path, hygiene report path, fresh-clone evidence path, unchanged legacy repository SHAs, and any remaining external prerequisite or GitHub App installation gap.
