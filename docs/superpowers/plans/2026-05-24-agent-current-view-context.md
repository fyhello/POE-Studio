# Agent 当前工作态上下文接入实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 让 POE Studio 的 Codex Agent 能理解并使用用户当前已打开的表格/对比视图，用户说“当前表格”“检查漏翻”时优先读取 UI 当前工作态，而不是重新读取 Native/GGPK bundle 或触发 Oodle。

**架构：** 前端在 `/api/chat` 请求中提交轻量 `currentView` 摘要；后端把摘要保存为短期 current-view 快照，并在 Codex prompt 中只传 `currentViewContextId` 和使用规则；MCP 新增只读 current-view 工具，Codex 按需查询当前表格上下文和漏翻候选。该方案保持 CODEX-first：POE Studio 提供项目能力和上下文工具，规划与调用顺序仍由 Codex 决定。

**技术栈：** ASP.NET Core Minimal API、C# record DTO、POE Studio MCP stdio tools、前端原生 JavaScript、xUnit。

---

## 0. 固定方向与硬约束

本计划修复的是 Agent 缺少“当前 UI 工作态”能力入口的问题，不是 Oodle 问题。

必须满足：

- 用户问“当前表格”“已打开表格”“检查漏翻”时，Agent 必须优先使用 current-view 工具。
- 不得把该类任务默认导向 `poe_datc64_extract_translatable_cells`。
- 不得为了当前表格检查要求用户配置 Oodle。
- 不得恢复旧 Agent Planner/Guard/Orchestrator/thread-run 框架。
- 不得把整张大表无脑灌入 prompt；prompt 只放摘要和 `currentViewContextId`。
- 写入仍走 overlay staging；本计划只做只读检查能力，不自动写草稿。
- 所有代码行为必须有计划、有进度、计划可追溯、进展可跟踪；执行者每完成一个任务必须勾选本文件对应步骤并提交。

## 1. 文件结构

### 新增文件

- `src/PoeStudio.Contracts/AgentCurrentViewDtos.cs`
  - 定义前端 current-view 请求 DTO、快照 DTO、漏翻候选 DTO。
- `src/PoeStudio.Storage/Agent/AgentCurrentViewStore.cs`
  - 将 current-view 快照写入 workspace 临时目录，供 MCP 子进程读取。
- `tests/PoeStudio.Tests/AgentCurrentViewStoreTests.cs`
  - 验证快照保存、读取、过期清理、安全路径边界。

### 修改文件

- `src/PoeStudio.Api/ChatService.cs`
  - `ChatRequest` 增加 `CurrentView`。
  - `ChatService` 保存快照，并把 `currentViewContextId` 注入 Codex prompt。
  - prompt 增加“当前表格优先使用 current-view 工具”的硬规则。
- `src/PoeStudio.Api/Program.cs`
  - 注入 `AgentCurrentViewStore`。
  - `/api/chat` 调用 `ChatService.RunCodexAsync` 时传入 `CurrentView`。
- `src/PoeStudio.Api/wwwroot/app.js`
  - 从 `state.selectedResource`、`state.tableEditBase`、`state.tableReference` 构建轻量 current-view。
  - `/api/chat` body 增加 `currentView`。
- `src/PoeStudio.Mcp/PoeMcpTools.cs`
  - 注册新 MCP 工具：
    - `poe_get_current_view_context`
    - `poe_find_current_table_untranslated_cells`
  - 工具从快照读取数据，不重新读 resource，不调用 Oodle。
- `tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs`
  - 验证 prompt 包含 current-view context id 和禁止默认 raw bundle read 的规则。
- `tests/PoeStudio.Tests/McpToolRegistryTests.cs`
  - 验证新工具 schema、描述、readOnly annotations。
- `tests/PoeStudio.Tests/McpPoeToolsTests.cs` 或新建 `tests/PoeStudio.Tests/McpCurrentViewToolTests.cs`
  - 验证 MCP 工具能读取 current-view 快照并返回漏翻候选。
- `tests/PoeStudio.Tests/FrontendDatc64WorkflowTests.cs`
  - 静态验证前端 chat 请求包含 currentView，且 currentView 从 `state.tableEditBase` / `state.tableReference` 构建。
- `docs/agent/poe-studio-agent-context.md`
  - 记录 Agent 当前工作态语义。
- `docs/agent/poe-studio-project-workflows.md`
  - 更新 Agent 工作流和工具选择规则。

---

## 2. 数据契约

实现者必须使用这些类型名，避免各任务之间名称漂移。

```csharp
namespace PoeStudio.Contracts;

public sealed record AgentCurrentViewRequestDto(
    string Kind,
    AgentCurrentTableViewDto? Table = null);

public sealed record AgentCurrentTableViewDto(
    string ProfileId,
    string ResourcePath,
    string? SourceProfileId,
    string? SourceResourcePath,
    string? TargetProfileId,
    string? TargetResourcePath,
    string Delimiter,
    int RowCount,
    int PreviewRowCount,
    IReadOnlyList<string> Columns,
    IReadOnlyList<int> EditableColumnIndexes,
    IReadOnlyList<AgentCurrentTableRowDto> TargetRows,
    IReadOnlyList<AgentCurrentTableRowDto>? SourceRows,
    string? ReferenceMatchMode);

public sealed record AgentCurrentTableRowDto(
    int RowNumber,
    IReadOnlyList<string> Cells);

public sealed record AgentCurrentViewSnapshotDto(
    string ContextId,
    DateTimeOffset CreatedAt,
    AgentCurrentViewRequestDto View);

public sealed record AgentUntranslatedCellDto(
    int RowNumber,
    int ColumnIndex,
    string? ColumnName,
    string SourceText,
    string TargetText,
    string Reason);
```

字段语义：

- `Kind` 第一版只允许 `"tableComparison"`、`"table"`、`"none"`。
- `TargetRows` 必须来自当前 UI 已解析的目标表，不允许重新读 resource。
- `SourceRows` 来自当前 UI 已匹配的来源参考表；如果没有参考表，则为 `null`。
- 第一版行数上限为 200 行，列数不裁剪；后续如需全量检查，再加分页工具，不在本计划扩大范围。

---

## 任务 1：新增 current-view DTO

**文件：**

- 创建：`src/PoeStudio.Contracts/AgentCurrentViewDtos.cs`
- 测试：通过后续任务测试覆盖

- [x] **步骤 1：创建 DTO 文件**

添加 `src/PoeStudio.Contracts/AgentCurrentViewDtos.cs`：

```csharp
namespace PoeStudio.Contracts;

public sealed record AgentCurrentViewRequestDto(
    string Kind,
    AgentCurrentTableViewDto? Table = null);

public sealed record AgentCurrentTableViewDto(
    string ProfileId,
    string ResourcePath,
    string? SourceProfileId,
    string? SourceResourcePath,
    string? TargetProfileId,
    string? TargetResourcePath,
    string Delimiter,
    int RowCount,
    int PreviewRowCount,
    IReadOnlyList<string> Columns,
    IReadOnlyList<int> EditableColumnIndexes,
    IReadOnlyList<AgentCurrentTableRowDto> TargetRows,
    IReadOnlyList<AgentCurrentTableRowDto>? SourceRows,
    string? ReferenceMatchMode);

public sealed record AgentCurrentTableRowDto(
    int RowNumber,
    IReadOnlyList<string> Cells);

public sealed record AgentCurrentViewSnapshotDto(
    string ContextId,
    DateTimeOffset CreatedAt,
    AgentCurrentViewRequestDto View);

public sealed record AgentUntranslatedCellDto(
    int RowNumber,
    int ColumnIndex,
    string? ColumnName,
    string SourceText,
    string TargetText,
    string Reason);
```

- [x] **步骤 2：编译验证**

运行：

```powershell
dotnet build PoeStudio.sln --no-restore
```

预期：编译通过。

- [ ] **步骤 3：Commit**

```powershell
git add src/PoeStudio.Contracts/AgentCurrentViewDtos.cs
git commit -m "feat(agent): add current view DTOs"
```

---

## 任务 2：新增 current-view 快照存储

**文件：**

- 创建：`src/PoeStudio.Storage/Agent/AgentCurrentViewStore.cs`
- 创建：`tests/PoeStudio.Tests/AgentCurrentViewStoreTests.cs`

- [x] **步骤 1：编写失败测试**

创建 `tests/PoeStudio.Tests/AgentCurrentViewStoreTests.cs`：

```csharp
using PoeStudio.Contracts;
using PoeStudio.Storage.Agent;

namespace PoeStudio.Tests;

public sealed class AgentCurrentViewStoreTests
{
    [Fact]
    public async Task SaveAsync_persists_snapshot_and_LoadAsync_reads_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-current-view-" + Guid.NewGuid().ToString("N"));
        var store = new AgentCurrentViewStore(root);
        var request = new AgentCurrentViewRequestDto(
            "tableComparison",
            new AgentCurrentTableViewDto(
                "target-profile",
                "data/balance/traditional chinese/activeskills.datc64",
                "source-profile",
                "data/balance/simplified chinese/activeskills.datc64",
                "target-profile",
                "data/balance/traditional chinese/activeskills.datc64",
                "datc64-auto",
                RowCount: 1,
                PreviewRowCount: 1,
                Columns: ["Id", "Name"],
                EditableColumnIndexes: [1],
                TargetRows: [new AgentCurrentTableRowDto(1, ["skill", "Fireball"])],
                SourceRows: [new AgentCurrentTableRowDto(1, ["skill", "火球"])],
                ReferenceMatchMode: "简体路径"));

        var snapshot = await store.SaveAsync(request, CancellationToken.None);
        var loaded = await store.LoadAsync(snapshot.ContextId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.ContextId, loaded.ContextId);
        Assert.Equal("tableComparison", loaded.View.Kind);
        Assert.Equal("Fireball", loaded.View.Table!.TargetRows[0].Cells[1]);
    }

    [Fact]
    public async Task LoadAsync_rejects_unsafe_context_id()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-current-view-" + Guid.NewGuid().ToString("N"));
        var store = new AgentCurrentViewStore(root);

        var loaded = await store.LoadAsync("../outside", CancellationToken.None);

        Assert.Null(loaded);
    }
}
```

- [x] **步骤 2：运行测试验证失败**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentCurrentViewStoreTests"
```

预期：编译失败或测试失败，提示 `AgentCurrentViewStore` 不存在。

- [x] **步骤 3：实现存储**

创建 `src/PoeStudio.Storage/Agent/AgentCurrentViewStore.cs`：

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using PoeStudio.Contracts;

namespace PoeStudio.Storage.Agent;

public sealed class AgentCurrentViewStore
{
    private static readonly Regex SafeIdPattern = new("^[a-f0-9]{32}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string root;

    public AgentCurrentViewStore(string workspaceRoot)
    {
        root = Path.Combine(workspaceRoot, "agent", "current-view");
    }

    public async Task<AgentCurrentViewSnapshotDto> SaveAsync(
        AgentCurrentViewRequestDto view,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        var contextId = Guid.NewGuid().ToString("N");
        var snapshot = new AgentCurrentViewSnapshotDto(contextId, DateTimeOffset.UtcNow, view);
        var path = PathFor(contextId);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken);
        return snapshot;
    }

    public async Task<AgentCurrentViewSnapshotDto?> LoadAsync(
        string? contextId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contextId) || !SafeIdPattern.IsMatch(contextId))
        {
            return null;
        }

        var path = PathFor(contextId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AgentCurrentViewSnapshotDto>(stream, JsonOptions, cancellationToken);
    }

    private string PathFor(string contextId) => Path.Combine(root, contextId + ".json");
}
```

- [x] **步骤 4：运行测试验证通过**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentCurrentViewStoreTests"
```

预期：`2/2 passed`。

- [ ] **步骤 5：Commit**

```powershell
git add src/PoeStudio.Storage/Agent/AgentCurrentViewStore.cs tests/PoeStudio.Tests/AgentCurrentViewStoreTests.cs
git commit -m "feat(agent): store current view snapshots"
```

---

## 任务 3：前端构建并提交 currentView

**文件：**

- 修改：`src/PoeStudio.Api/wwwroot/app.js`
- 修改：`tests/PoeStudio.Tests/FrontendDatc64WorkflowTests.cs`

- [x] **步骤 1：编写失败测试**

在 `tests/PoeStudio.Tests/FrontendDatc64WorkflowTests.cs` 增加测试：

```csharp
[Fact]
public void Chat_request_includes_current_table_view_context()
{
    var repoRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        ".."));
    var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));

    Assert.Contains("function buildAgentCurrentView()", appJs);
    Assert.Contains("state.tableEditBase", appJs);
    Assert.Contains("state.tableReference", appJs);
    Assert.Contains("currentView: buildAgentCurrentView()", appJs);
    Assert.Contains("targetRows: summarizeAgentTableRows(state.tableEditBase?.rows", appJs);
    Assert.Contains("sourceRows: summarizeAgentTableRows(state.tableReference?.inspection?.rows", appJs);
}
```

- [x] **步骤 2：运行测试验证失败**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~FrontendDatc64WorkflowTests.Chat_request_includes_current_table_view_context"
```

预期：FAIL，找不到 `buildAgentCurrentView`。

- [x] **步骤 3：实现前端摘要函数**

在 `src/PoeStudio.Api/wwwroot/app.js` 中 `tableInspectLimitForResource` 附近或 chat 函数之前新增：

```javascript
const agentCurrentViewRowLimit = 200;

function summarizeAgentTableRows(rows, limit = agentCurrentViewRowLimit) {
  return (rows || []).slice(0, limit).map(function (row) {
    return {
      rowNumber: row.rowNumber,
      cells: (row.cells || []).map(function (cell) { return String(cell ?? ""); })
    };
  });
}

function buildAgentCurrentView() {
  if (!state.selectedResource || !state.tableEditBase) {
    return { kind: "none" };
  }

  const table = state.tableEditBase;
  const reference = state.tableReference;
  const sourceId = reference?.resource?.profileId ?? sourceProfileId() ?? null;
  const sourcePath = reference?.resource?.virtualPath ?? null;
  const targetId = state.selectedResource.profileId;
  const targetPath = state.selectedResource.virtualPath;
  const kind = reference?.inspection ? "tableComparison" : "table";

  return {
    kind,
    table: {
      profileId: targetId,
      resourcePath: targetPath,
      sourceProfileId: sourceId,
      sourceResourcePath: sourcePath,
      targetProfileId: targetId,
      targetResourcePath: targetPath,
      delimiter: table.delimiter || "",
      rowCount: table.rowCount || table.previewRowCount || 0,
      previewRowCount: table.previewRowCount || 0,
      columns: table.columns || [],
      editableColumnIndexes: table.editableColumnIndexes || [],
      targetRows: summarizeAgentTableRows(state.tableEditBase?.rows),
      sourceRows: reference?.inspection?.rows ? summarizeAgentTableRows(state.tableReference?.inspection?.rows) : null,
      referenceMatchMode: reference?.matchMode || null
    }
  };
}
```

- [x] **步骤 4：把 currentView 加入 `/api/chat` body**

在 `startChat` 的 `JSON.stringify({ ... })` 中增加：

```javascript
currentView: buildAgentCurrentView()
```

确保字段位置在 `targetResourcePath` 后，保留现有 source/target path 字段。

- [x] **步骤 5：运行前端静态测试**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~FrontendDatc64WorkflowTests.Chat_request_includes_current_table_view_context"
```

预期：PASS。

- [ ] **步骤 6：Commit**

```powershell
git add src/PoeStudio.Api/wwwroot/app.js tests/PoeStudio.Tests/FrontendDatc64WorkflowTests.cs
git commit -m "feat(agent): send current table view to chat"
```

---

## 任务 4：ChatService 保存快照并约束 prompt

**文件：**

- 修改：`src/PoeStudio.Api/ChatService.cs`
- 修改：`src/PoeStudio.Api/Program.cs`
- 修改：`tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs`

- [x] **步骤 1：编写失败测试**

在 `tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs` 增加测试：

```csharp
[Fact]
public async Task RunCodexAsync_includes_current_view_context_id_and_current_table_rule()
{
    string? capturedPrompt = null;
    var runner = new FakeCodexRunner((settings, prompt, onEvent, ct) =>
    {
        capturedPrompt = prompt;
        return Task.FromResult(new CodexRunResult(0, false, false, [], null));
    });
    var root = Path.Combine(Path.GetTempPath(), "poe-chat-current-view-" + Guid.NewGuid().ToString("N"));
    var service = new ChatService(runner, CreateWorkspaceRoot(root), BuildConfig(), new AgentCurrentViewStore(root));
    var view = new AgentCurrentViewRequestDto(
        "tableComparison",
        new AgentCurrentTableViewDto(
            "target",
            "data/balance/traditional chinese/activeskills.datc64",
            "source",
            "data/balance/simplified chinese/activeskills.datc64",
            "target",
            "data/balance/traditional chinese/activeskills.datc64",
            "datc64-auto",
            1,
            1,
            ["Id", "Name"],
            [1],
            [new AgentCurrentTableRowDto(1, ["skill", "Fireball"])],
            [new AgentCurrentTableRowDto(1, ["skill", "火球"])],
            "简体路径"));

    await foreach (var _ in service.RunCodexAsync(
        "检查当前表格漏翻",
        "target",
        "data/balance/traditional chinese/activeskills.datc64",
        "source",
        "target",
        "data/balance/simplified chinese/activeskills.datc64",
        "data/balance/traditional chinese/activeskills.datc64",
        view,
        CancellationToken.None))
    {
    }

    Assert.NotNull(capturedPrompt);
    Assert.Contains("currentViewContextId:", capturedPrompt);
    Assert.Contains("When the user says current table", capturedPrompt);
    Assert.Contains("poe_get_current_view_context", capturedPrompt);
    Assert.Contains("poe_find_current_table_untranslated_cells", capturedPrompt);
    Assert.Contains("Do not call poe_datc64_extract_translatable_cells for current table checks", capturedPrompt);
}
```

如果现有 `CreateWorkspaceRoot` helper 不接受 root 参数，按测试文件现有 helper 风格新增一个重载。

- [x] **步骤 2：运行测试验证失败**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~ChatServiceIntegrationTests.RunCodexAsync_includes_current_view_context_id_and_current_table_rule"
```

预期：编译失败，`ChatService` 构造函数和 `RunCodexAsync` 尚不支持 current view。

- [x] **步骤 3：修改 ChatRequest 和 ChatService**

在 `src/PoeStudio.Api/ChatService.cs`：

1. 增加 using：

```csharp
using PoeStudio.Contracts;
using PoeStudio.Storage.Agent;
```

2. 修改 `ChatRequest`：

```csharp
public sealed record ChatRequest(
    string Message,
    string? ProfileId = null,
    string? ResourcePath = null,
    string? SourceProfileId = null,
    string? TargetProfileId = null,
    string? SourceResourcePath = null,
    string? TargetResourcePath = null,
    AgentCurrentViewRequestDto? CurrentView = null);
```

3. 给 `ChatService` 增加字段和构造参数：

```csharp
private readonly AgentCurrentViewStore _currentViewStore;

public ChatService(
    ICodexProcessRunner runner,
    WorkspaceRootProvider workspaceRoot,
    IConfiguration configuration,
    AgentCurrentViewStore currentViewStore)
{
    _runner = runner;
    _workspaceRoot = workspaceRoot;
    _configuration = configuration;
    _currentViewStore = currentViewStore;
}
```

4. 修改 `RunCodexAsync` 签名，增加 `AgentCurrentViewRequestDto? currentView`。

5. 在构建 prompt 前保存快照：

```csharp
AgentCurrentViewSnapshotDto? snapshot = null;
if (currentView is not null && !string.Equals(currentView.Kind, "none", StringComparison.OrdinalIgnoreCase))
{
    snapshot = await _currentViewStore.SaveAsync(currentView, cancellationToken);
}
```

如果当前方法不能 `await`，将 `RunCodexAsync` 改为返回 async iterator 或新增私有 `RunCodexCoreAsync`。不要用 `.Result` 或 `.GetAwaiter().GetResult()`。

6. `BuildPrompt` 增加 `AgentCurrentViewSnapshotDto? currentViewSnapshot` 参数，并追加：

```csharp
if (currentViewSnapshot is not null)
{
    lines.Add($"- currentViewContextId: {currentViewSnapshot.ContextId}");
    lines.Add($"- currentViewKind: {currentViewSnapshot.View.Kind}");
    lines.Add("When the user says current table, currently opened table, current draft, current comparison, or asks to check missing translations, call poe_get_current_view_context or poe_find_current_table_untranslated_cells first.");
    lines.Add("Do not call poe_datc64_extract_translatable_cells for current table checks unless the user explicitly asks to reread the raw resource from storage.");
}
```

- [x] **步骤 4：修改 Program.cs DI 和路由调用**

在 `src/PoeStudio.Api/Program.cs` 注册：

```csharp
builder.Services.AddSingleton(sp => new AgentCurrentViewStore(sp.GetRequiredService<WorkspaceRootProvider>().CurrentRoot));
```

如果 `WorkspaceRootProvider.CurrentRoot` 可能运行时变化，改为工厂封装或在 `AgentCurrentViewStore` 构造中接收 `Func<string>`。第一版允许使用当前 workspace root，但测试必须覆盖。

在 `/api/chat` 路由调用 `RunCodexAsync` 时传入：

```csharp
request.CurrentView
```

- [x] **步骤 5：运行 ChatService 测试**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~ChatServiceIntegrationTests"
```

预期：全部通过。

- [ ] **步骤 6：Commit**

```powershell
git add src/PoeStudio.Api/ChatService.cs src/PoeStudio.Api/Program.cs tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs
git commit -m "feat(agent): persist current view for codex runs"
```

---

## 任务 5：MCP 注册 current-view 工具

**文件：**

- 修改：`src/PoeStudio.Mcp/PoeMcpTools.cs`
- 修改：`tests/PoeStudio.Tests/McpToolRegistryTests.cs`

- [x] **步骤 1：编写失败测试**

在 `tests/PoeStudio.Tests/McpToolRegistryTests.cs` 增加：

```csharp
[Fact]
public async Task Tools_list_includes_current_view_tools_as_read_only()
{
    var registry = new McpToolRegistry();
    PoeMcpTools.RegisterAll(registry, new PoeWorkspaceResolution(true, Path.GetTempPath(), "test", null), new NativeBundleResourceContentResolver(new MissingOodleCodec()));

    var tools = registry.ListTools();

    var getContext = Assert.Single(tools, tool => tool.Name == "poe_get_current_view_context");
    var findCells = Assert.Single(tools, tool => tool.Name == "poe_find_current_table_untranslated_cells");
    Assert.Contains("current UI", getContext.Description, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("missing translations", findCells.Description, StringComparison.OrdinalIgnoreCase);
    Assert.True(getContext.Annotations?.ReadOnlyHint);
    Assert.True(findCells.Annotations?.ReadOnlyHint);
}
```

如 `MissingOodleCodec` 在该测试不可见，按同文件现有测试方式使用已有 fake resolver。

- [x] **步骤 2：运行测试验证失败**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~McpToolRegistryTests.Tools_list_includes_current_view_tools_as_read_only"
```

预期：FAIL，工具不存在。

- [x] **步骤 3：注册工具 schema**

在 `src/PoeStudio.Mcp/PoeMcpTools.cs` 的 `RegisterAll` 中新增：

```csharp
registry.Register(
    new McpToolDefinition(
        "poe_get_current_view_context",
        "Read the current UI view snapshot provided by POE Studio chat. Use this before reading raw resources when the user refers to the current table, current draft, or current comparison. Does not read Native/GGPK bundles and does not require Oodle.",
        ObjectSchema(("contextId", "string")),
        ReadOnlyAnnotations),
    (arguments, cancellationToken) => GetCurrentViewContextAsync(workspace, arguments, cancellationToken));

registry.Register(
    new McpToolDefinition(
        "poe_find_current_table_untranslated_cells",
        "Find likely missing translations from the current UI table comparison snapshot. Uses already-opened target/source rows; does not read raw resources, Native/GGPK bundles, or Oodle.",
        ObjectSchema(("contextId", "string"), ("limit", "integer")),
        ReadOnlyAnnotations),
    (arguments, cancellationToken) => FindCurrentTableUntranslatedCellsAsync(workspace, arguments, cancellationToken));
```

- [x] **步骤 4：运行注册测试**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~McpToolRegistryTests.Tools_list_includes_current_view_tools_as_read_only"
```

预期：PASS。

- [ ] **步骤 5：Commit**

```powershell
git add src/PoeStudio.Mcp/PoeMcpTools.cs tests/PoeStudio.Tests/McpToolRegistryTests.cs
git commit -m "feat(mcp): register current view tools"
```

---

## 任务 6：实现 MCP current-view 工具行为

**文件：**

- 修改：`src/PoeStudio.Mcp/PoeMcpTools.cs`
- 创建：`tests/PoeStudio.Tests/McpCurrentViewToolTests.cs`

- [x] **步骤 1：编写失败测试**

创建 `tests/PoeStudio.Tests/McpCurrentViewToolTests.cs`：

```csharp
using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Storage.Agent;

namespace PoeStudio.Tests;

public sealed class McpCurrentViewToolTests
{
    [Fact]
    public async Task Find_current_table_untranslated_cells_uses_snapshot_without_oodle()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-mcp-current-view-" + Guid.NewGuid().ToString("N"));
        var store = new AgentCurrentViewStore(root);
        var snapshot = await store.SaveAsync(new AgentCurrentViewRequestDto(
            "tableComparison",
            new AgentCurrentTableViewDto(
                "target",
                "data/balance/traditional chinese/activeskills.datc64",
                "source",
                "data/balance/simplified chinese/activeskills.datc64",
                "target",
                "data/balance/traditional chinese/activeskills.datc64",
                "datc64-auto",
                3,
                3,
                ["Id", "Name"],
                [1],
                [
                    new AgentCurrentTableRowDto(1, ["same", "火球"]),
                    new AgentCurrentTableRowDto(2, ["english", "Lightning Warp"]),
                    new AgentCurrentTableRowDto(3, ["empty", ""])
                ],
                [
                    new AgentCurrentTableRowDto(1, ["same", "火球"]),
                    new AgentCurrentTableRowDto(2, ["english", "闪电传送"]),
                    new AgentCurrentTableRowDto(3, ["empty", "冰霜新星"])
                ],
                "简体路径")), CancellationToken.None);

        var registry = McpToolRegistry.CreateDefault(
            new PoeWorkspaceResolution(true, root, "test", null),
            new NativeBundleResourceContentResolver(new MissingOodleCodec()));

        var result = await registry.CallToolAsync(
            "poe_find_current_table_untranslated_cells",
            JsonDocument.Parse($$"""{"contextId":"{{snapshot.ContextId}}","limit":10}""").RootElement,
            CancellationToken.None);

        Assert.False(result.IsError);
        var text = Assert.Single(result.Content).Text;
        Assert.Contains("Lightning Warp", text);
        Assert.Contains("冰霜新星", text);
        Assert.DoesNotContain("native_oodle_missing", text);
    }
}
```

- [x] **步骤 2：运行测试验证失败**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~McpCurrentViewToolTests"
```

预期：FAIL，工具 handler 未实现。

- [x] **步骤 3：实现 `GetCurrentViewContextAsync`**

在 `PoeMcpTools.cs` 增加：

```csharp
private static async Task<McpToolResult> GetCurrentViewContextAsync(
    PoeWorkspaceResolution workspace,
    JsonElement arguments,
    CancellationToken cancellationToken)
{
    if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
    {
        return McpToolResult.Error(error);
    }

    if (!TryGetString(arguments, "contextId", out var contextId))
    {
        return McpToolResult.Error("Argument 'contextId' is required.");
    }

    var snapshot = await new AgentCurrentViewStore(workspaceRoot).LoadAsync(contextId, cancellationToken);
    if (snapshot is null)
    {
        return McpToolResult.Error($"Current view context '{contextId}' was not found.");
    }

    return JsonSuccess(snapshot);
}
```

- [x] **步骤 4：实现漏翻检测**

在 `PoeMcpTools.cs` 增加：

```csharp
private static async Task<McpToolResult> FindCurrentTableUntranslatedCellsAsync(
    PoeWorkspaceResolution workspace,
    JsonElement arguments,
    CancellationToken cancellationToken)
{
    if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
    {
        return McpToolResult.Error(error);
    }

    if (!TryGetString(arguments, "contextId", out var contextId))
    {
        return McpToolResult.Error("Argument 'contextId' is required.");
    }

    var limit = Math.Clamp(GetInt32(arguments, "limit") ?? 50, 1, 200);
    var snapshot = await new AgentCurrentViewStore(workspaceRoot).LoadAsync(contextId, cancellationToken);
    var table = snapshot?.View.Table;
    if (table is null)
    {
        return McpToolResult.Error($"Current view context '{contextId}' does not contain a table.");
    }

    if (table.SourceRows is null || table.SourceRows.Count == 0)
    {
        return McpToolResult.Error("Current table has no source/reference rows. Ask the user to open or match a source table first.");
    }

    var sourceRows = table.SourceRows.ToDictionary(row => row.RowNumber);
    var editable = table.EditableColumnIndexes.Count > 0
        ? table.EditableColumnIndexes
        : Enumerable.Range(0, table.Columns.Count).ToArray();
    var results = new List<AgentUntranslatedCellDto>();

    foreach (var targetRow in table.TargetRows)
    {
        if (!sourceRows.TryGetValue(targetRow.RowNumber, out var sourceRow))
        {
            continue;
        }

        foreach (var columnIndex in editable)
        {
            var sourceText = CellAt(sourceRow, columnIndex);
            var targetText = CellAt(targetRow, columnIndex);
            if (string.IsNullOrWhiteSpace(sourceText))
            {
                continue;
            }

            var reason = MissingTranslationReason(sourceText, targetText);
            if (reason is null)
            {
                continue;
            }

            results.Add(new AgentUntranslatedCellDto(
                targetRow.RowNumber,
                columnIndex,
                columnIndex >= 0 && columnIndex < table.Columns.Count ? table.Columns[columnIndex] : null,
                sourceText,
                targetText,
                reason));

            if (results.Count >= limit)
            {
                break;
            }
        }

        if (results.Count >= limit)
        {
            break;
        }
    }

    return JsonSuccess(new
    {
        snapshot.ContextId,
        table.TargetProfileId,
        table.TargetResourcePath,
        table.SourceProfileId,
        table.SourceResourcePath,
        inspectedRows = table.TargetRows.Count,
        candidates = results.Count,
        items = results
    });
}

private static string CellAt(AgentCurrentTableRowDto row, int columnIndex)
{
    return columnIndex >= 0 && columnIndex < row.Cells.Count ? row.Cells[columnIndex] : string.Empty;
}

private static string? MissingTranslationReason(string sourceText, string targetText)
{
    if (string.IsNullOrWhiteSpace(targetText))
    {
        return "target_empty";
    }

    if (string.Equals(sourceText, targetText, StringComparison.Ordinal))
    {
        return null;
    }

    if (LooksMostlyAscii(targetText) && !LooksMostlyAscii(sourceText))
    {
        return "target_still_english";
    }

    return null;
}

private static bool LooksMostlyAscii(string value)
{
    var letters = value.Where(char.IsLetter).ToArray();
    if (letters.Length == 0)
    {
        return false;
    }

    var asciiLetters = letters.Count(ch => ch <= 0x7f);
    return asciiLetters >= Math.Ceiling(letters.Length * 0.8);
}
```

注意：这里按用户当前规则保守处理。

- 两边一样：不报。
- 目标为空、来源有内容：报。
- 来源明显非英文，目标仍主要为英文：报。
- 其他复杂差异先不报，避免误报；后续可加“疑似未同步”分类。

- [x] **步骤 5：运行 MCP 工具测试**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~McpCurrentViewToolTests"
```

预期：PASS。

- [ ] **步骤 6：Commit**

```powershell
git add src/PoeStudio.Mcp/PoeMcpTools.cs tests/PoeStudio.Tests/McpCurrentViewToolTests.cs
git commit -m "feat(mcp): read current table context"
```

---

## 任务 7：确保“当前表格漏翻”不默认触发 raw DATC64 工具

**文件：**

- 修改：`tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs`
- 修改：`src/PoeStudio.Api/ChatService.cs`

- [x] **步骤 1：编写失败测试**

在 `ChatServiceIntegrationTests.cs` 增加：

```csharp
[Fact]
public async Task Prompt_directs_current_table_missing_translation_to_current_view_tools()
{
    string? capturedPrompt = null;
    var runner = new FakeCodexRunner((settings, prompt, onEvent, ct) =>
    {
        capturedPrompt = prompt;
        return Task.FromResult(new CodexRunResult(0, false, false, [], null));
    });
    var root = Path.Combine(Path.GetTempPath(), "poe-current-table-rule-" + Guid.NewGuid().ToString("N"));
    var service = new ChatService(runner, CreateWorkspaceRoot(root), BuildConfig(), new AgentCurrentViewStore(root));
    var view = new AgentCurrentViewRequestDto(
        "tableComparison",
        new AgentCurrentTableViewDto("target", "target.datc64", "source", "source.datc64", "target", "target.datc64", "datc64-auto", 1, 1, ["Text"], [0], [new AgentCurrentTableRowDto(1, ["Fireball"])], [new AgentCurrentTableRowDto(1, ["火球"])], "简体路径"));

    await foreach (var _ in service.RunCodexAsync("检查当前表格漏翻的内容", "target", "target.datc64", "source", "target", "source.datc64", "target.datc64", view, CancellationToken.None))
    {
    }

    Assert.NotNull(capturedPrompt);
    Assert.Contains("poe_find_current_table_untranslated_cells", capturedPrompt);
    Assert.Contains("Do not call poe_datc64_extract_translatable_cells for current table checks", capturedPrompt);
}
```

- [x] **步骤 2：运行测试验证失败或通过**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~ChatServiceIntegrationTests.Prompt_directs_current_table_missing_translation_to_current_view_tools"
```

预期：如果任务 4 prompt 已覆盖，直接 PASS；否则 FAIL。

- [x] **步骤 3：补强 prompt**

如果失败，在 `ChatService.BuildPrompt` current-view 段补充：

```csharp
lines.Add("For requests like '检查当前表格漏翻', 'check missing translations in current table', or '当前表格有没有漏翻', call poe_find_current_table_untranslated_cells with currentViewContextId.");
lines.Add("Only call raw resource tools such as poe_read_resource or poe_datc64_extract_translatable_cells when currentViewContextId is absent or the user explicitly asks to reread raw files.");
```

- [x] **步骤 4：运行测试**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~ChatServiceIntegrationTests.Prompt_directs_current_table_missing_translation_to_current_view_tools"
```

预期：PASS。

- [ ] **步骤 5：Commit**

```powershell
git add src/PoeStudio.Api/ChatService.cs tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs
git commit -m "fix(agent): prefer current view for current table tasks"
```

---

## 任务 8：文档更新

**文件：**

- 修改：`docs/agent/poe-studio-agent-context.md`
- 修改：`docs/agent/poe-studio-project-workflows.md`

- [x] **步骤 1：更新 Agent 上下文文档**

在 `docs/agent/poe-studio-agent-context.md` 的 Agent 架构段增加：

```markdown
### 当前工作态上下文

用户说“当前表格”“当前草稿”“当前对比视图”时，Agent 必须理解为 UI 当前工作态，而不是底层资源文件。前端会在 `/api/chat` 中提交 `currentView` 摘要，后端保存为短期 `currentViewContextId`，Codex 通过 MCP 工具读取。

可用工具：

- `poe_get_current_view_context`：读取当前 UI 工作态快照。
- `poe_find_current_table_untranslated_cells`：基于当前已打开目标表和来源参考表查找漏翻候选。

规则：

- 当前表格检查优先使用 current-view 工具。
- 不要默认调用 `poe_datc64_extract_translatable_cells`，因为该工具读取底层资源，可能触发 Native/GGPK/Oodle，并且不代表 UI 当前工作态。
- 只有用户明确要求重新读取原始资源时，才使用 raw resource 工具。
```

- [x] **步骤 2：更新项目工作流文档**

在 `docs/agent/poe-studio-project-workflows.md` 的 MCP 工具表增加：

```markdown
| `poe_get_current_view_context` | 读取当前 UI 工作态快照 | “当前表格是什么？”“当前打开了什么？” | `contextId` | 不读底层资源，不触发 Oodle | 只读；快照有时效 |
| `poe_find_current_table_untranslated_cells` | 基于当前目标/来源表快照查找漏翻候选 | “检查当前表格漏翻。” | `contextId`、`limit` | 使用 UI 已解析表格，不重新解压 bundle | 只读；第一版最多检查快照中的前 200 行 |
```

在工作流中增加：

```markdown
自然语言“当前表格漏翻”流程：

1. UI 提交 `currentView`。
2. ChatService 保存 current-view snapshot。
3. Prompt 暴露 `currentViewContextId`。
4. Codex 调用 `poe_find_current_table_untranslated_cells`。
5. UI 展示候选；不写 overlay。
```

- [ ] **步骤 3：Commit**

```powershell
git add docs/agent/poe-studio-agent-context.md docs/agent/poe-studio-project-workflows.md
git commit -m "docs(agent): document current view workflow"
```

---

## 任务 9：全量验证与实机验收

**文件：**

- 不新增文件；记录结果可追加到本计划末尾“执行记录”。

- [x] **步骤 1：构建**

运行：

```powershell
dotnet build PoeStudio.sln --no-restore
```

预期：0 errors。

- [x] **步骤 2：全量测试**

运行：

```powershell
dotnet test PoeStudio.sln --no-restore --no-build
```

预期：全部通过。

- [x] **步骤 3：GitNexus 变更检测**

运行：

```powershell
npx gitnexus analyze
```

然后使用 GitNexus：

```text
gitnexus_detect_changes(scope="all")
```

预期：由于 Agent 重构仍可能是 HIGH/CRITICAL，但新增 current-view 改动必须只集中在 ChatService、PoeMcpTools、前端 chat/current table、测试和文档，不应出现 patch build 或 native writer 生产逻辑变更。

- [x] **步骤 4：启动服务**

运行：

```powershell
dotnet run --project src/PoeStudio.Api --urls http://localhost:5010
```

预期：`Now listening on: http://localhost:5010`。

- [ ] **步骤 5：实机验收**

在浏览器中：

1. 打开 `http://localhost:5010`。
2. 选择来源 profile：简体。
3. 选择目标 profile：国际服/目标。
4. 打开目标表：`data/balance/traditional chinese/activeskills.datc64`。
5. 确认 UI 已显示目标表并匹配来源表。
6. 打开 AI Chat。
7. 输入：`检查当前表格漏翻的内容`。

验收必须满足：

- UI 显示 tool call：`poe_find_current_table_untranslated_cells`。
- 不应调用：`poe_datc64_extract_translatable_cells`。
- 不应出现：`native_oodle_missing`。
- 回复内容应基于当前表格和来源表，列出候选或说明没有候选。
- 不创建 overlay，除非用户另行要求写入。

- [ ] **步骤 6：最终 Commit**

如果任务 9 只更新执行记录：

```powershell
git add docs/superpowers/plans/2026-05-24-agent-current-view-context.md
git commit -m "docs(agent): record current view context acceptance"
```

---

## 10. 自检

规格覆盖：

- “当前表格不应需要 Oodle”：任务 3、4、5、6、7、9 覆盖。
- “自然语言交互，不是脚本入口”：Codex 仍自行选择 MCP 工具；POE Studio 只提供 current-view 能力，任务 4、5 覆盖。
- “不把大量项目知识灌入 prompt”：任务 4 使用 `currentViewContextId`，任务 6 按需工具读取。
- “不恢复旧 Agent 框架”：全计划未修改旧 Planner/Guard/Orchestrator。
- “计划可追溯”：本文件复选框 + 每任务 commit。

已知边界：

- 第一版 current-view 快照只包含前 200 行，解决当前实机验证和避免 prompt/token 爆炸。后续如果要全表漏翻检查，应新增分页/游标工具，而不是扩大 prompt。
- 第一版漏翻判断保守：目标为空、来源非英文而目标英文。复杂差异留到后续“翻译质量检测”阶段。
- 如果 UI 尚未打开表格或尚未匹配来源表，工具必须返回可操作错误，要求用户先打开/匹配，而不是转去读 Native bundle。

## 执行记录

- 2026-05-24：已完成任务 1-8 的实现与测试，任务 9 完成构建、全量测试、GitNexus analyze/detect_changes、服务启动和浏览器加载检查。
- 验证命令：`dotnet build PoeStudio.sln --no-restore`，结果 0 warnings / 0 errors。
- 验证命令：`dotnet test PoeStudio.sln --no-restore --no-build`，结果 414/414 passed。
- GitNexus：`npx gitnexus analyze` 显示 Already up to date；`detect_changes(scope="all")` 返回 CRITICAL，因为当前工作树已有大量未提交 Agent/runner/前端改动，本次 current-view 增量集中在 DTO/store、ChatService、MCP current-view 工具、前端 chat 摘要、测试和文档。
- 服务验收：`dotnet run --project src/PoeStudio.Api --urls http://localhost:5010 --no-build` 已监听 `http://localhost:5010`；浏览器加载 POE Studio 首页成功。
- 实机资源验收：未完成。浏览器快照显示当前资源树为 0 且未选择资源，因此未执行 `data/balance/traditional chinese/activeskills.datc64` 的真实 UI 选表和 chat tool-call 验收。
- Commit：未执行。当前分支在开始前已有大量未提交/未跟踪/删除改动，且涉及本计划同名文件；为避免把既有工作误打包进提交，本次只完成实现和验证，不按任务拆分提交。
