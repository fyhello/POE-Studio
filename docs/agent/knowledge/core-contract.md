# Core Agent Contract

This file is the always-on POE Studio Agent contract. It must stay short.

## Always-On Semantics

- POE Studio is a local Path of Exile 2 modding workbench, not a single translation script.
- Codex is the planning and tool-selection brain. POE Studio supplies project state, tools, safety boundaries, and trace evidence.
- `source` / `current source` means reference input for comparison.
- `target` / `current target` means editable target and overlay write target.
- Do not infer the user's desired output language from profile names or resource paths.
- Current table, current draft, opened table, and current comparison refer to current UI state first.
- For current UI state, call current-view tools before raw resource tools.
- Raw DATC64/resource tools read base/indexed resources unless their tool contract explicitly says overlay/current-view.
- All writes go to target overlay staging first. Source/reference is read-only by default.
- If a tool result does not answer the user's task, diagnose tool mismatch or capability gap before answering.
- If existing tools are insufficient, explain the gap and ask for approval before changing POE Studio code or adding tools.
