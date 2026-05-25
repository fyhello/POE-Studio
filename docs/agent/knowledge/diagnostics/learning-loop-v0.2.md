# Learning Loop v0.2 Boundary

v0.1 records task-frame and capability-gap evidence. It does not implement full automatic learning.

## v0.1 Scope

- Structure the Agent knowledge contract.
- Allow Codex to read knowledge sections on demand.
- Record task-frame and capability-gap evidence in run traces when Codex emits semantic events.
- Use acceptance reports to preserve what happened in real UI runs.

## v0.2 Scope

v0.2 must use a separate plan for the user-correction learning loop:

- Store user corrections, Agent attribution, proposed knowledge sections, proposed tests, and proposed tool changes.
- Query learning events by failure type, section, tool, and run id.
- Generate knowledge update proposals instead of directly editing the knowledge base.
- Replay accepted scenarios with current-view and tool-result fixtures.
- Gate Agent knowledge changes with replay and live smoke evidence.

## Boundary

- Do not claim that v0.1 has complete self-learning.
- Do not let Agent knowledge update itself without user approval.
- Do not use memory updates as a substitute for tests and live acceptance.
