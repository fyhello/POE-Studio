# Current View Workflow

## Meaning

When the user says current table, current draft, opened table, or current comparison, use the UI snapshot first.

## Required Tools

- `poe_get_current_view_context`
- Current-table analysis tools that explicitly read current-view snapshots.

## Rules

- Do not reread raw DATC64/Oodle resources before inspecting current-view when `currentViewContextId` exists.
- Current-view target rows are the editable target state currently visible in UI.
- Current-view source rows are reference rows for comparison.
- A current-view read-only check must not write overlay.
