# POE Studio MCP Stage 1 验收报告

验收日期：2026-05-22

验收范围：POE Studio MCP Tools Stage 1。仅验证只读 MCP stdio 工具接入、workspace/profile/index/resource/DATC64 读取能力，以及 native Bundles2 资源边界。

## 结论

Stage 1 修复后通过验收。Codex CLI 可以发现并调用 `poe-studio` MCP server；必需工具已注册；真实调用证据覆盖 profile、index、resource search、DATC64 extraction 和 native resource boundary。`poe_read_resource` 已补强 physical path allowed-roots 校验，不再仅信任索引中的 `PhysicalPath`。未实现 UI、Agent API、Codex app-server、写 overlay 或任意写工具。

## 环境

- Codex version: `codex-cli 0.131.0`
- Branch: `codex/poe-mcp-stage1`
- Worktree: `C:\Users\25147\Documents\AI-xiangmu\POE-Studio\.worktrees\poe-mcp-stage1`
- POE Studio workspace: `C:\Users\25147\AppData\Local\PoeStudio`
- MCP server name: `poe-studio`

## MCP 实现模式

MCP implementation mode: manual fallback

Fallback reason:

- SDK package version: `ModelContextProtocol` `1.3.0`
- SDK failure command: `dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~McpProtocolTests`
- SDK failure output summary: official SDK path passed `initialize`、`notifications/initialized` 和 unknown method cases, but malformed JSON parse-error coverage timed out instead of emitting the expected stdout JSON-RPC parse error.
- Reason this is not usage error: package restore and install were valid; logging was kept off stdout; registration ordering and initialization flow were checked; malformed JSON handling remained incompatible with the required Codex stdio acceptance behavior. The project therefore keeps the SDK package as a documented reference and uses the manual JSON-RPC fallback for Stage 1.

## MCP 注册证据

Initial registration used the repository root from the plan:

```powershell
codex mcp add poe-studio -- dotnet run --project src\PoeStudio.Mcp\PoeStudio.Mcp.csproj -- --workspace-root "C:\Users\25147\Documents\AI-xiangmu\POE-Studio"
```

That server started and could receive MCP calls, but the repository root has no real POE Studio profile data. The real workspace was confirmed from `%LOCALAPPDATA%\PoeStudio\workspace-settings.json`:

```json
{"workspaceRoot":"C:\\Users\\25147\\AppData\\Local\\PoeStudio"}
```

The final registered command is:

```text
poe-studio
  enabled: true
  transport: stdio
  command: dotnet
  args: run --project src\PoeStudio.Mcp\PoeStudio.Mcp.csproj -- --workspace-root C:\Users\25147\AppData\Local\PoeStudio
```

`codex mcp list` includes:

```text
poe-studio  dotnet  run --project src\PoeStudio.Mcp\PoeStudio.Mcp.csproj -- --workspace-root C:\Users\25147\AppData\Local\PoeStudio  enabled
```

## Codex MCP 调用证据

Evidence file: `%TEMP%\poe-mcp-stage1-profiles-real.jsonl`

Observed JSON events:

- `mcp_tool_call` server `poe-studio`, tool `poe_list_profiles`
- `mcp_tool_call` server `poe-studio`, tool `poe_get_index_status`

Result summary:

- Workspace root: `C:\Users\25147\AppData\Local\PoeStudio`
- Profiles returned: `7`
- Target profile: `75c5bef9860a45658cbb2a41aae5c057`
- Target display name: `国际服-目标`
- Index exists: `true`
- Index format: `sharded`
- Resource count: `2,699,101`
- Indexed at: `2026-05-21T12:08:42.9185139+00:00`

## DATC64 工具样例验收

Evidence file: `%TEMP%\poe-mcp-stage1-datc64-fixture.jsonl`

The acceptance used a physical fixture workspace because the large real profiles mostly expose `.datc64` entries as native/non-physical `ggpk-bundles2://` resources. Stage 1 intentionally rejects those until a safe native read service is integrated.

- Fixture workspace: `%TEMP%\poe-mcp-stage1-acceptance-fixture`
- Fixture profile: `fixture-profile`
- Resource path: `metadata/agent-ui.datc64`
- Search result total: `1`
- Extracted cell count: `4`

Sample `sourceText` values:

1. `NoMana`
2. `法力不足`
3. `OnCooldown`

The tool returned row, column, column name, `sourceText`, and locator fields for each cell.

## Native Resource 边界验收

Evidence file: `%TEMP%\poe-mcp-stage1-native-real.jsonl`

Command intent:

```text
Use poe_read_resource for profile 75c5bef9860a45658cbb2a41aae5c057 and resource data/balance/clientstrings.datc64.
```

Observed result:

```text
native_resource_not_supported_in_stage1: Native Bundles2 or non-physical resources are not supported by Stage 1 MCP read tools.
```

This satisfies the Stage 1 boundary: native Bundles2 resources do not return empty successful content.

## Physical Path 边界修复验收

Review finding:

```text
poe_read_resource could read any existing indexed PhysicalPath after Path.GetFullPath and File.Exists.
```

Fix summary:

- `PoeResourceContentReader` now requires explicit allowed physical roots.
- `poe_read_resource` and `poe_datc64_extract_translatable_cells` derive allowed roots from the target profile:
  - `RootPath`
  - `Bundles2Path`
  - `ContentGgpkPath` parent directory
- Indexed physical files outside those roots return `physical_path_outside_allowed_roots` and are not read.

Regression evidence:

```text
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~McpResourceContentReaderTests|FullyQualifiedName~McpPoeToolsTests|FullyQualifiedName~McpDatc64ToolTests"
Result: 22/22 passed
```

Added coverage:

- `McpResourceContentReaderTests.ReadAsync_rejects_indexed_physical_path_outside_allowed_roots`
- `McpPoeToolsTests.Read_resource_rejects_indexed_physical_path_outside_profile_roots`

## 写入边界

Before report creation, `git status --short` was clean. Codex MCP acceptance calls were read-only. No overlay draft, game resource, POE client file, `/api/agent/*`, `/api/codex/*`, UI, Codex app-server, shell execution tool, arbitrary file write tool, or real overlay write was added by Stage 1.

## 测试证据

MCP regression filter after process smoke test:

```text
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~Mcp"
Result: 31/31 passed
```

Build verification:

```text
dotnet build PoeStudio.sln --no-restore
Result: 0 warnings, 0 errors
```

Full suite:

```text
dotnet test PoeStudio.sln --no-restore
Result after physical path boundary fix: 347/347 passed
```

## Commit 证据

Completed Stage 1 implementation commits:

- `6c5cd62 feat(mcp): add POE Studio MCP project shell`
- `3b78fcd feat(mcp): implement stdio protocol lifecycle`
- `8bcfac8 feat(mcp): register POE tool schemas`
- `b5256d4 feat(mcp): resolve POE workspace root`
- `70c3eb3 feat(mcp): expose workspace profile and index tools`
- `b89ad9a feat(mcp): expose resource search and read tools`
- `1f85b6d feat(mcp): extract DATC64 translatable cells`
- `b96487e test(mcp): verify stdio process handshake`
- `c6340ef docs(mcp): record Stage 1 acceptance evidence`
- this commit: `fix(mcp): constrain physical resource reads to profile roots`

Stage 1 status: PASS
