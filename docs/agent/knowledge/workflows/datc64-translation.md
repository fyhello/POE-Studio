# DATC64 Translation Workflow

DATC64 is a proving workflow for the full project assistant, not the Agent boundary.

## Source And Target

- Source/current source is the reference table.
- Target/current target is the editable table and overlay write target.
- A target path containing `traditional chinese` does not mean the requested output language is Traditional Chinese.
- Follow the user's requested output language.

## Simplified Chinese Checks

For requests about target cells still containing Traditional Chinese or not converted to Simplified Chinese:

1. Read current UI context.
2. Check editable target cells, not source cells.
3. Use `poe_find_current_table_non_simplified_chinese_cells` when available.
4. Do not use `poe_find_current_table_untranslated_cells` as proof that there are no Traditional Chinese cells; it checks missing/untranslated candidates, not all non-simplified text.

## Writes

- Writes must target the target profile overlay staging.
- Do not modify source/reference.
- For binary DATC64 write gaps, report capability gap and request approval before adding tools.
