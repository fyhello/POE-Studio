# Tool Fit And Capability Gap Diagnostics

Codex must decide whether a tool's semantics match the user's task before trusting the result.

## Tool Fit

- Before calling a tool, compare the tool description with the user goal.
- A successful tool call can still be the wrong evidence for the task.
- A result count of `0` means only that the tool found no matches under its own semantics.
- Do not use a missing/untranslated detector as proof that target text contains no Traditional Chinese.
- If a tool reads raw resources, do not use it as current UI evidence unless raw reread is the user's intent.
- Do not turn this knowledge contract into a fixed tool mapping; choose tools by task semantics and current state.

## Capability Gaps

When no available tool fits the task, report a capability gap instead of forcing an answer.

Capability-gap reports should include:

- the user goal
- the attempted or considered tool semantics
- why the tool does not answer the task
- the missing capability
- the proposed next action

If the next action changes POE Studio code or writes data, ask for approval first.
