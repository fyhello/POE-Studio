# Overlay Staging Workflow

Overlay staging is the draft write layer for POE Studio edits.

## Rules

- Write tools save draft content to the target profile overlay staging area.
- Write tools never directly modify base game files.
- Source/reference profiles are read-only unless the user explicitly changes the editable target.
- Users review overlays before patch build.
- List, diff, audit, and revert overlay operations are review operations.
- Patch build consumes overlay manifest entries; it should not silently create unrelated overlay writes.
