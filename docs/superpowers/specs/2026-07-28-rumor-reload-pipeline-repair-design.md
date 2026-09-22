# Rumor Reload Pipeline Repair Design

## Problem

The passive-system campaign proved that native social producers and initial World
History ingestion work, but rumor processing stops after loading a save.

Every session launch resets the native World History behavior to not ready. A
matching persisted campaign-readiness seal currently releases the campaign
without reopening the timeline. Consequently:

- later World History and `social_outcome` events are silently discarded;
- opening the timeline never resumes pending server-side social workers;
- World Test reports zero rumor hooks and exposure attempts;
- save rollback does not establish the expected isolated active timeline.

The failed BaseThree run contained 63 pending social outcomes, including five
valid `strong_captain` candidates. Those records belong to the abandoned test
future and must not be replayed into a replacement test timeline.

## Selected Approach

Use a branch-aware timeline handshake as a mandatory prerequisite to releasing
any sealed campaign load.

A simple unconditional release remains invalid because the native behavior is
not ready. Merely reopening the timeline and retaining the old readiness seal is
also invalid when the server creates a branch for a rolled-back save. Rerunning
the complete initialization pipeline on every ordinary load would be safe but
would impose unnecessary delay.

## Load Behavior

### Same-timeline resume

Before using a matching readiness seal:

1. Open the saved World History timeline with the saved sequence and head.
2. Restore the native transport's ready state.
3. Resume pending server-side social-outcome processing.
4. Confirm that the returned timeline matches the sealed timeline.
5. Release the campaign without rerunning identity, snapshot, or relationship
   initialization.

### Rolled-back or recovered timeline

If the server returns a different timeline:

1. Treat the old readiness seal as invalid for the new timeline.
2. Persist the returned timeline as the active save timeline.
3. Preserve completed personality and native-foundation work.
4. Rerun identity synchronization, authoritative snapshots, relationship
   reconciliation, critical queue drain, and server acknowledgement.
5. Release only after the new timeline receives a valid readiness seal.

The abandoned future remains on its old timeline and is excluded from the new
test. Pending social work is resumed only for the timeline actually opened.

## Social-Event Compatibility Cleanup

Remove the obsolete client call to `/rumors/public_phase_finished`. That route
belonged to the retired propagation-based rumor system and no longer exists on
the server. Current and future social-event producers must submit structured
`social_outcome` evidence through World History rather than reconstructing
rumors from a completed public transcript.

## Observability

World Test must distinguish:

- qualifying native hooks;
- exposure attempts;
- successful exposed occurrences;
- active Rumors;
- promoted Reputations;
- pending and failed social-outcome processing.

A failed probability roll must still increment eligible-hook and exposure
counts. This ensures a quiet rumor list cannot conceal a broken transport or
worker.

## Failure Handling

- Timeline handshake failure blocks campaign release and surfaces through the
  existing readiness retry flow.
- A stale asynchronous response cannot mutate a newer campaign generation.
- A returned blank timeline is an initialization failure.
- A branch cannot reuse the previous timeline's server acknowledgement.
- Pending social work remains idempotent through existing outcome receipts.

## Verification

Automated regression coverage will prove:

1. A sealed load still requires a timeline handshake.
2. A same-timeline handshake permits the fast release path.
3. A changed timeline invalidates downstream completion and server
   acknowledgement.
4. Branch recovery resumes at identity initialization.
5. Social outcomes continue to emit after reload.
6. Opening a timeline resumes pending social workers.
7. World Test counts eligible hooks and exposure attempts independently of
   successful exposure.
8. The retired public-phase endpoint is no longer called.

Validation will use the Reign MCP `all` profile because the change crosses the
Bannerlord client, server worker, World Test reporting, and shared readiness
contracts. Deployment will use the supported stopped-system lifecycle. The
passive-system task will start a fresh campaign after deployment; this repair
will not start that test automatically.
