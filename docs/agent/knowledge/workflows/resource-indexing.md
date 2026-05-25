# Resource Indexing Workflow

Resource indexing describes what a profile can read. It does not define the user's task intent.

## Semantics

- A profile is client/workspace context, not a language or output intent.
- A resource path is a virtual path inside the indexed client resources.
- Raw read tools read base/index resources unless their contract explicitly says overlay-aware or current-view.
- Native/GGPK/Oodle errors are dependency or read-layer issues, not automatic task failure.
- If a task depends on current UI edits, prefer current-view or overlay-aware reads before raw resource reads.
- If a resource is missing from the index, diagnose profile/index state before assuming the resource does not exist.
