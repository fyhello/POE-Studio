# POE Studio Agent 自诊断与自修复闭环实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 让 POE Studio 内置 Codex Agent 在工具调用、项目知识、运行时桥接或代码缺陷出问题时，能够自动诊断问题、给出证据、请求用户授权，并在授权后完成修复、测试、重启和原任务回归验证。

**架构：** 保持 CODEX-first 薄桥接方向：POE Studio 不恢复旧 Planner/Guard/Orchestrator，不替 Codex 做任务规划；POE Studio 只提供运行事件、诊断上下文、项目工具和审批边界。Agent 运行分为普通任务 run、自动诊断 run、用户批准后的 repair run 三类，异常路径必须可追踪、可展示、可恢复。

**技术栈：** ASP.NET Core Minimal API、C# record DTO、Codex CLI `exec --json`、POE Studio MCP stdio tools、原生 JavaScript SSE UI、xUnit、GitNexus 影响分析。

---

## 0. 硬约束

- [ ] **H0.1：不得恢复旧 Agent 框架。** 禁止恢复 `AgentPlannerPromptBuilder`、`AgentOrchestrator`、`AgentPlanGuardService`、`AgentStore` 这类 POE Studio 自建规划框架。
- [ ] **H0.2：普通业务任务不得开放代码修改能力。** 默认 `/api/chat` 只允许业务 MCP 工具，不允许 shell / 文件修改 / 项目代码编辑。
- [ ] **H0.3：自修复必须二段式授权。** 诊断 run 只能提出修复方案；只有用户点击“批准修复”后，repair run 才允许 Codex 修改代码、运行测试、重启服务。
- [ ] **H0.4：current-view 方向不回退。** “当前表格 / 当前草稿 / 当前对比视图”必须继续优先使用 `poe_get_current_view_context` 和 `poe_find_current_table_untranslated_cells`，不得退回默认 raw DATC64/Oodle 读取。
- [ ] **H0.5：所有 run 必须有可追溯计划与进度。** 每次普通 run、诊断 run、repair run 都必须落事件日志，前端能看到当前阶段、调用工具、失败原因、下一步。
- [ ] **H0.6：修复代码前必须保护工作区。** repair run 启动前记录 `git status --short --branch`、当前 commit、工作树 dirty 文件；若存在未提交用户改动，必须在 UI 明示风险，并限制修复只触碰本次批准范围。
- [ ] **H0.7：必须区分 runMode。** 所有 run 必须标记 `normal`、`diagnostic` 或 `repair`。只有 `normal` run 可以自动触发诊断；`diagnostic` 和 `repair` run 绝不能递归触发新的自动诊断。
- [ ] **H0.8：工具悬挂必须有等待阈值。** `tool_call_left_in_progress` 只有在同一工具开始后至少 30 秒仍无 completed/failed 事件时才能判定，不允许 1-2 秒误判。
- [ ] **H0.9：诊断前必须释放前端发送锁。** 当系统准备启动自动诊断 run 时，必须先向前端发送带 `autoDiagnostic: true` 的 `done` 事件，让用户可继续输入并能看到诊断面板。
- [ ] **H0.10：repair run 的 Codex 能力必须显式配置。** repair 模式必须明确 `Memories=false`、`Skills=false`、`CommandExecution=true`、`Sandbox=workspace-write`、`ApprovalMode=never`，不得沿用普通业务 run 的禁用命令配置。

---

## 1. 文件结构

### 新增文件

- `src/PoeStudio.Contracts/AgentRunDtos.cs`
  - 定义薄桥接 run/事件/诊断/审批 DTO，不恢复旧 thread/run 复杂模型。
- `src/PoeStudio.Storage/Agent/AgentRunTraceStore.cs`
  - 将 chat run 的事件保存为 JSONL：prompt 摘要、Codex JSON 事件、SSE 事件、MCP 工具状态、错误、诊断结论。
- `src/PoeStudio.Contracts/AgentDiagnosticsDtos.cs`
  - 定义诊断摘要、异常类型、修复建议、批准请求 DTO。
- `src/PoeStudio.Api/AgentDiagnosticsService.cs`
  - 根据 run trace 判断异常：工具悬挂、无最终回答、MCP error、Codex timeout、前端 SSE 断流。
- `src/PoeStudio.Api/AgentRepairService.cs`
  - 在用户批准后启动 Codex repair run，提供工程能力 prompt 和受控工作目录。
- `tests/PoeStudio.Tests/AgentRunTraceStoreTests.cs`
  - 验证事件持久化、读取、裁剪、敏感信息过滤。
- `tests/PoeStudio.Tests/AgentDiagnosticsServiceTests.cs`
  - 验证异常分类和自动诊断触发条件。
- `tests/PoeStudio.Tests/AgentRepairServiceTests.cs`
  - 验证 repair run 必须有用户批准、会记录 git 状态、会输出测试结果。

### 修改文件

- `src/PoeStudio.Api/ChatService.cs`
  - 创建 runId，保存 trace，检测异常后触发自动诊断 run，不再只把事件流原样转发。
- `src/PoeStudio.Core/Agent/CodexJsonEventParser.cs`
  - MCP completed 事件必须解析 `result.content` 文本摘要，供 UI 展示和诊断使用。
- `src/PoeStudio.Core/Agent/CodexProcessRunner.cs`
  - 增加 terminal/no-final-answer 识别；保留薄 runner，不加业务规划。
- `src/PoeStudio.Mcp/PoeMcpTools.cs`
  - 增加只读诊断工具：读取 run trace、工具清单、最近 POE Studio 日志摘要。
- `src/PoeStudio.Mcp/McpProtocol.cs`
  - instructions 区分普通业务模式和诊断模式；不得继续一刀切禁止所有工程诊断能力。
- `src/PoeStudio.Api/Program.cs`
  - 注册新服务，新增诊断和修复审批 API。
- `src/PoeStudio.Api/wwwroot/app.js`
  - 前端展示 run 状态、工具完成结果、诊断结论、批准修复按钮、repair run 进度。
- `src/PoeStudio.Api/wwwroot/index.html`
  - Chat UI 增加诊断面板和修复审批区。
- `src/PoeStudio.Api/wwwroot/styles.css`
  - 增加工具结果、诊断状态、审批按钮样式。
- `tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs`
  - 覆盖 no-final-answer、tool-hang、auto-diagnosis、repair approval。
- `tests/PoeStudio.Tests/McpToolRegistryTests.cs`
  - 覆盖新增诊断工具 schema。
- `tests/PoeStudio.Tests/McpPoeToolsTests.cs`
  - 覆盖诊断工具行为。
- `tests/PoeStudio.Tests/FrontendDatc64WorkflowTests.cs`
  - 静态覆盖前端诊断 UI、发送锁释放、工具结果展示。

---

## 2. 成功标准

- [ ] 用户问“列出当前表格 3 个漏翻内容”，Agent 调用 current-view 工具后必须输出 3 条具体结果或明确说明未发现。
- [ ] 如果某个 MCP 工具调用没有 completed 事件，UI 必须显示“工具调用超时/悬挂”，并自动启动诊断。
- [ ] 工具悬挂检测必须基于 `lastEventAt` 和 30 秒阈值；小于阈值不得触发诊断。
- [ ] 如果 Codex turn completed 但没有最终回答，系统必须自动启动诊断 run，诊断 run 要引用上一 run 的 runId 和事件证据。
- [ ] 诊断 run 必须能回答“断在哪一层”：前端 SSE、ChatService、CodexProcessRunner、MCP 工具、业务数据、项目知识。
- [ ] 诊断 run 自身失败时只能输出诊断失败，不得再次启动诊断。
- [ ] 诊断 run 不能直接改代码，只能给出修复提案。
- [ ] 用户批准后，repair run 才能使用工程能力，必须运行定向测试和全量或相关回归测试。
- [ ] repair run 完成后必须自动回到原始用户任务做一次回归验证。

---

## 任务 1：修通工具结果事件闭环

**文件：**
- 修改：`src/PoeStudio.Core/Agent/CodexJsonEventParser.cs`
- 修改：`src/PoeStudio.Api/ChatService.cs`
- 修改：`src/PoeStudio.Api/wwwroot/app.js`
- 测试：`tests/PoeStudio.Tests/CodexJsonEventParserTests.cs`
- 测试：`tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs`
- 测试：`tests/PoeStudio.Tests/FrontendDatc64WorkflowTests.cs`

- [ ] **步骤 1：编写失败测试：MCP completed 携带 result 摘要**

在 `CodexJsonEventParserTests.cs` 添加：

```csharp
[Fact]
public void ParseLine_extracts_completed_mcp_result_content()
{
    var parsed = _parser.ParseLine("""
        {"type":"item.completed","item":{"type":"mcp_tool_call","server":"poe-studio","tool":"poe_find_current_table_untranslated_cells","arguments":{"limit":3},"result":{"content":[{"type":"text","text":"{\"candidates\":3,\"items\":[{\"rowNumber\":1,\"sourceText\":\"火球\",\"targetText\":\"\"}]}"}]},"status":"completed"}}
        """);

    Assert.Equal(CodexParsedEventType.McpToolCall, parsed.EventType);
    Assert.Contains("\"candidates\":3", parsed.PayloadJson);
    Assert.Contains("火球", parsed.PayloadJson);
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~CodexJsonEventParserTests.ParseLine_extracts_completed_mcp_result_content"
```

预期：FAIL，`PayloadJson` 当前不包含 MCP result content。

- [ ] **步骤 3：实现最小解析**

在 `CodexJsonEventParser.ParseMcpToolCall` 中将 payload 扩展为：

```csharp
var resultText = GetResultContentText(item);
var payload = new
{
    server,
    tool,
    arguments = TryGetRaw(item, "arguments"),
    status,
    error,
    resultText
};
```

要求：失败工具仍保留 error；成功工具 resultText 可为空但字段存在。

- [ ] **步骤 4：ChatService SSE 透传 resultText**

在 `ParseToolCallEvent` 中读取 `resultText`：

```csharp
var resultText = root.TryGetProperty("resultText", out var r) && r.ValueKind == JsonValueKind.String
    ? r.GetString()
    : null;
```

SSE payload 增加：

```csharp
resultText
```

- [ ] **步骤 5：前端工具卡片更新完成态与结果摘要**

在 `app.js` 中修改 `addChatToolCall`：

```javascript
function addChatToolCall(tool, argsInput, status, resultText = null) {
  const existing = findOpenToolCall(tool, argsInput);
  const card = existing || createChatToolCallCard(tool, argsInput);
  card.querySelector(".chat-tool-status").textContent = status || "pending";
  if (resultText) {
    card.querySelector(".chat-tool-result").textContent = resultText.slice(0, 2000);
  }
}
```

新增辅助函数：

```javascript
function findOpenToolCall(tool, argsInput) {
  const key = chatToolCallKey(tool, argsInput);
  return document.querySelector(`[data-tool-call-key="${CSS.escape(key)}"]`);
}

function chatToolCallKey(tool, argsInput) {
  return `${tool}:${JSON.stringify(argsInput || {})}`;
}
```

- [ ] **步骤 6：运行定向测试**

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "CodexJsonEventParserTests|ChatServiceIntegrationTests|FrontendDatc64WorkflowTests"
```

预期：PASS。

- [ ] **步骤 7：Commit**

```powershell
git add src/PoeStudio.Core/Agent/CodexJsonEventParser.cs src/PoeStudio.Api/ChatService.cs src/PoeStudio.Api/wwwroot/app.js tests/PoeStudio.Tests/CodexJsonEventParserTests.cs tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs tests/PoeStudio.Tests/FrontendDatc64WorkflowTests.cs
git commit -m "fix(agent): surface MCP tool results in chat stream"
```

---

## 任务 2：建立 run trace，可让 Agent 看见自己哪里断了

**文件：**
- 创建：`src/PoeStudio.Contracts/AgentRunDtos.cs`
- 创建：`src/PoeStudio.Storage/Agent/AgentRunTraceStore.cs`
- 修改：`src/PoeStudio.Api/ChatService.cs`
- 测试：`tests/PoeStudio.Tests/AgentRunTraceStoreTests.cs`
- 测试：`tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs`

- [ ] **步骤 1：编写失败测试：保存和读取 run 事件**

创建 `AgentRunTraceStoreTests.cs`：

```csharp
using PoeStudio.Contracts;
using PoeStudio.Storage.Agent;

namespace PoeStudio.Tests;

public sealed class AgentRunTraceStoreTests
{
    [Fact]
    public async Task Append_and_read_run_events_in_order()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new AgentRunTraceStore(root);
        var runId = Guid.NewGuid().ToString("N");

        await store.AppendAsync(runId, new AgentRunTraceEventDto("message", "started", "{}", DateTimeOffset.UtcNow), CancellationToken.None);
        await store.AppendAsync(runId, new AgentRunTraceEventDto("tool_call", "completed", "{\"tool\":\"poe_get_workspace\"}", DateTimeOffset.UtcNow), CancellationToken.None);

        var events = await store.ReadAsync(runId, CancellationToken.None);

        Assert.Equal(["message", "tool_call"], events.Select(x => x.EventName).ToArray());
    }
}
```

- [ ] **步骤 2：创建 DTO**

在 `AgentRunDtos.cs`：

```csharp
namespace PoeStudio.Contracts;

public sealed record AgentRunStartedDto(
    string RunId,
    DateTimeOffset StartedAt,
    string Mode,
    string Message);

public sealed record AgentRunTraceEventDto(
    string EventName,
    string Status,
    string DataJson,
    DateTimeOffset CreatedAt);

public static class AgentRunModes
{
    public const string Normal = "normal";
    public const string Diagnostic = "diagnostic";
    public const string Repair = "repair";
}
```

- [ ] **步骤 3：实现 trace store**

在 `AgentRunTraceStore.cs`：

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using PoeStudio.Contracts;

namespace PoeStudio.Storage.Agent;

public sealed class AgentRunTraceStore
{
    private static readonly Regex SafeId = new("^[a-f0-9]{32}$", RegexOptions.Compiled);
    private readonly string root;

    public AgentRunTraceStore(string workspaceRoot)
    {
        root = Path.Combine(workspaceRoot, "agent", "runs");
    }

    public async Task AppendAsync(string runId, AgentRunTraceEventDto evt, CancellationToken cancellationToken)
    {
        if (!SafeId.IsMatch(runId)) throw new ArgumentException("Invalid run id.", nameof(runId));
        Directory.CreateDirectory(root);
        await File.AppendAllTextAsync(Path.Combine(root, runId + ".jsonl"), JsonSerializer.Serialize(evt) + Environment.NewLine, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentRunTraceEventDto>> ReadAsync(string runId, CancellationToken cancellationToken)
    {
        if (!SafeId.IsMatch(runId)) return [];
        var path = Path.Combine(root, runId + ".jsonl");
        if (!File.Exists(path)) return [];
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        return lines.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => JsonSerializer.Deserialize<AgentRunTraceEventDto>(x, new JsonSerializerOptions(JsonSerializerDefaults.Web))!)
            .ToArray();
    }
}
```

- [ ] **步骤 4：ChatService 写 trace**

`ChatService.RunInternalAsync` 开始时创建：

```csharp
var runId = Guid.NewGuid().ToString("N");
var runMode = AgentRunModes.Normal;
await _traceStore.AppendAsync(runId, new AgentRunTraceEventDto("run", "started", JsonSerializer.Serialize(new { runMode, message }), DateTimeOffset.UtcNow), cancellationToken);
```

每次 `ConvertToSseEvents` 产生 SSE 事件后写入 trace：

```csharp
await _traceStore.AppendAsync(runId, new AgentRunTraceEventDto(sseEvent.EventName, "observed", sseEvent.DataJson, DateTimeOffset.UtcNow), cancellationToken);
```

首个 SSE 事件发给前端：

```csharp
Sse("run", new { type = "run_started", runId })
```

- [ ] **步骤 4.1：ChatService 记录 Codex 原始事件**

在 `_runner.RunAsync` 的 `onEvent` 回调中，除 SSE 事件外还必须写入原始 Codex 事件摘要，确保后续能判断“Codex 没有产生 item.completed”还是“产生了但桥接没转发”：

```csharp
await _traceStore.AppendAsync(runId, new AgentRunTraceEventDto(
    "codex_event",
    parsedEvent.EventType.ToString(),
    JsonSerializer.Serialize(new
    {
        parsedEvent.EventType,
        parsedEvent.Message,
        parsedEvent.ToolName,
        parsedEvent.IsTerminal,
        parsedEvent.PayloadJson
    }),
    DateTimeOffset.UtcNow),
    cancellationToken);
```

预期：run trace 中同时能看到 `codex_event` 和对应的 `tool_call` SSE 事件。若只有 `codex_event` 无 SSE，问题在 ChatService 转发；若两者都没有 completed，问题在 Codex/MCP 运行链路。

- [ ] **步骤 5：注册 DI**

在 `Program.cs`：

```csharp
builder.Services.AddScoped(sp => new AgentRunTraceStore(sp.GetRequiredService<WorkspaceRootProvider>().CurrentRoot));
```

- [ ] **步骤 6：运行测试**

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "AgentRunTraceStoreTests|ChatServiceIntegrationTests"
```

预期：PASS。

- [ ] **步骤 7：Commit**

```powershell
git add src/PoeStudio.Contracts/AgentRunDtos.cs src/PoeStudio.Storage/Agent/AgentRunTraceStore.cs src/PoeStudio.Api/ChatService.cs src/PoeStudio.Api/Program.cs tests/PoeStudio.Tests/AgentRunTraceStoreTests.cs tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs
git commit -m "feat(agent): persist chat run trace events"
```

---

## 任务 3：自动诊断异常 run

**文件：**
- 创建：`src/PoeStudio.Contracts/AgentDiagnosticsDtos.cs`
- 创建：`src/PoeStudio.Api/AgentDiagnosticsService.cs`
- 修改：`src/PoeStudio.Api/ChatService.cs`
- 测试：`tests/PoeStudio.Tests/AgentDiagnosticsServiceTests.cs`
- 测试：`tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs`

- [ ] **步骤 1：编写失败测试：无最终回答触发诊断**

创建 `AgentDiagnosticsServiceTests.cs`：

```csharp
using PoeStudio.Api;
using PoeStudio.Contracts;

namespace PoeStudio.Tests;

public sealed class AgentDiagnosticsServiceTests
{
    [Fact]
    public void Analyze_detects_tool_completed_without_final_answer()
    {
        var events = new[]
        {
            new AgentRunTraceEventDto("tool_call", "observed", "{\"tool\":\"poe_find_current_table_untranslated_cells\",\"status\":\"completed\"}", DateTimeOffset.UtcNow),
            new AgentRunTraceEventDto("done", "observed", "{\"type\":\"completed\"}", DateTimeOffset.UtcNow)
        };

        var result = AgentDiagnosticsService.Analyze("run-1", events);

        Assert.Equal("no_final_answer_after_tool_result", result.Code);
        Assert.True(result.ShouldStartDiagnosticRun);
    }
}
```

- [ ] **步骤 1.1：编写失败测试：工具悬挂必须等待 30 秒**

在 `AgentDiagnosticsServiceTests.cs` 增加：

```csharp
[Fact]
public void Analyze_does_not_mark_recent_tool_call_as_hung_before_threshold()
{
    var now = DateTimeOffset.UtcNow;
    var events = new[]
    {
        new AgentRunTraceEventDto("run", "started", "{\"runMode\":\"normal\"}", now.AddSeconds(-5)),
        new AgentRunTraceEventDto("tool_call", "observed", "{\"tool\":\"poe_find_current_table_untranslated_cells\",\"status\":\"in_progress\"}", now.AddSeconds(-5))
    };

    var result = AgentDiagnosticsService.Analyze("run-1", events, now, TimeSpan.FromSeconds(30));

    Assert.Equal("none", result.Code);
    Assert.False(result.ShouldStartDiagnosticRun);
}

[Fact]
public void Analyze_marks_tool_call_as_hung_after_threshold()
{
    var now = DateTimeOffset.UtcNow;
    var events = new[]
    {
        new AgentRunTraceEventDto("run", "started", "{\"runMode\":\"normal\"}", now.AddSeconds(-40)),
        new AgentRunTraceEventDto("tool_call", "observed", "{\"tool\":\"poe_find_current_table_untranslated_cells\",\"status\":\"in_progress\"}", now.AddSeconds(-40))
    };

    var result = AgentDiagnosticsService.Analyze("run-1", events, now, TimeSpan.FromSeconds(30));

    Assert.Equal("tool_call_left_in_progress", result.Code);
    Assert.True(result.ShouldStartDiagnosticRun);
}
```

- [ ] **步骤 1.2：编写失败测试：诊断 run 不递归触发诊断**

在 `AgentDiagnosticsServiceTests.cs` 增加：

```csharp
[Fact]
public void Analyze_never_auto_diagnoses_diagnostic_or_repair_runs()
{
    var now = DateTimeOffset.UtcNow;
    var diagnosticEvents = new[]
    {
        new AgentRunTraceEventDto("run", "started", "{\"runMode\":\"diagnostic\"}", now.AddSeconds(-60)),
        new AgentRunTraceEventDto("tool_call", "observed", "{\"tool\":\"poe_get_agent_run_trace\",\"status\":\"in_progress\"}", now.AddSeconds(-60))
    };

    var result = AgentDiagnosticsService.Analyze("diag-1", diagnosticEvents, now, TimeSpan.FromSeconds(30));

    Assert.Equal("diagnostic_run_failed_no_recursion", result.Code);
    Assert.False(result.ShouldStartDiagnosticRun);
}
```

- [ ] **步骤 2：定义 DTO**

在 `AgentDiagnosticsDtos.cs`：

```csharp
namespace PoeStudio.Contracts;

public sealed record AgentDiagnosticFindingDto(
    string RunId,
    string Code,
    string Severity,
    string Summary,
    bool ShouldStartDiagnosticRun,
    IReadOnlyList<string> Evidence,
    string RunMode = "normal");
```

- [ ] **步骤 3：实现异常分类**

`AgentDiagnosticsService.Analyze` 必须识别：

```csharp
public static AgentDiagnosticFindingDto Analyze(
    string runId,
    IReadOnlyList<AgentRunTraceEventDto> events,
    DateTimeOffset now,
    TimeSpan toolHangThreshold)
{
    var runMode = DetectRunMode(events);
    if (runMode is "diagnostic" or "repair")
    {
        var hasProblem = events.Any(x => x.EventName == "tool_call" && x.DataJson.Contains("\"status\":\"in_progress\"", StringComparison.Ordinal));
        return hasProblem
            ? new AgentDiagnosticFindingDto(runId, $"{runMode}_run_failed_no_recursion", "high", $"{runMode} run failed, but recursive auto-diagnosis is disabled.", false, events.Select(x => $"{x.EventName}:{x.DataJson}").Take(10).ToArray(), runMode)
            : new AgentDiagnosticFindingDto(runId, "none", "info", "No agent run anomaly detected.", false, [], runMode);
    }

    var hasCompletedTool = events.Any(x => x.EventName == "tool_call" && x.DataJson.Contains("\"status\":\"completed\"", StringComparison.Ordinal));
    var hasAssistantMessageAfterTool = events
        .SkipWhile(x => !(x.EventName == "tool_call" && x.DataJson.Contains("\"status\":\"completed\"", StringComparison.Ordinal)))
        .Any(x => x.EventName == "message" && x.DataJson.Contains("agent_message", StringComparison.Ordinal));

    if (hasCompletedTool && !hasAssistantMessageAfterTool)
    {
        return new AgentDiagnosticFindingDto(runId, "no_final_answer_after_tool_result", "high", "MCP tool completed but Codex did not produce a user-facing final answer.", true, events.Select(x => $"{x.EventName}:{x.DataJson}").Take(10).ToArray(), runMode);
    }

    var latestOpenTool = events.LastOrDefault(x => x.EventName == "tool_call" && x.DataJson.Contains("\"status\":\"in_progress\"", StringComparison.Ordinal));
    var hasOpenTool = latestOpenTool is not null
        && now - latestOpenTool.CreatedAt >= toolHangThreshold
        && !events.Any(x => x.EventName == "tool_call" && (x.DataJson.Contains("\"status\":\"completed\"", StringComparison.Ordinal) || x.DataJson.Contains("\"status\":\"failed\"", StringComparison.Ordinal)));

    if (hasOpenTool)
    {
        return new AgentDiagnosticFindingDto(runId, "tool_call_left_in_progress", "high", "A tool call started but no completed/failed event was observed after the hang threshold.", true, events.Select(x => $"{x.EventName}:{x.DataJson}").Take(10).ToArray(), runMode);
    }

    return new AgentDiagnosticFindingDto(runId, "none", "info", "No agent run anomaly detected.", false, [], runMode);
}
```

新增 `DetectRunMode`：

```csharp
private static string DetectRunMode(IReadOnlyList<AgentRunTraceEventDto> events)
{
    var run = events.FirstOrDefault(x => x.EventName == "run");
    if (run is null) return "normal";
    using var doc = JsonDocument.Parse(run.DataJson);
    return doc.RootElement.TryGetProperty("runMode", out var mode) && mode.ValueKind == JsonValueKind.String
        ? mode.GetString() ?? "normal"
        : "normal";
}
```

- [ ] **步骤 4：ChatService 在 run 结束后分析**

在 finally 写 `done` 前读取当前 run trace 并分析：

```csharp
var trace = await _traceStore.ReadAsync(runId, CancellationToken.None);
var finding = AgentDiagnosticsService.Analyze(runId, trace, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(30));
if (runMode == AgentRunModes.Normal && finding.ShouldStartDiagnosticRun)
{
    await channel.Writer.WriteAsync(Sse("done", new { type = "completed", autoDiagnostic = true }), CancellationToken.None);
    await channel.Writer.WriteAsync(Sse("diagnostic", new { type = "diagnostic_started", finding }), CancellationToken.None);
}
```

此任务只发 diagnostic 事件，不启动第二个 Codex run；第二个 run 在任务 4 实现。注意：`done(autoDiagnostic=true)` 必须先发，确保前端释放发送锁并能展示诊断面板。

- [ ] **步骤 5：运行测试**

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "AgentDiagnosticsServiceTests|ChatServiceIntegrationTests"
```

预期：PASS。

- [ ] **步骤 6：Commit**

```powershell
git add src/PoeStudio.Contracts/AgentDiagnosticsDtos.cs src/PoeStudio.Api/AgentDiagnosticsService.cs src/PoeStudio.Api/ChatService.cs tests/PoeStudio.Tests/AgentDiagnosticsServiceTests.cs tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs
git commit -m "feat(agent): detect incomplete chat run outcomes"
```

---

## 任务 4：诊断 run 让 Codex 自己判断断点

**文件：**
- 修改：`src/PoeStudio.Api/ChatService.cs`
- 修改：`src/PoeStudio.Mcp/PoeMcpTools.cs`
- 修改：`src/PoeStudio.Mcp/McpProtocol.cs`
- 测试：`tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs`
- 测试：`tests/PoeStudio.Tests/McpToolRegistryTests.cs`
- 测试：`tests/PoeStudio.Tests/McpPoeToolsTests.cs`

- [ ] **步骤 1：新增 MCP 工具测试**

在 `McpToolRegistryTests.cs` 断言存在：

```csharp
Assert.Contains(tools, tool => tool.Name == "poe_get_agent_run_trace");
Assert.Contains(tools, tool => tool.Name == "poe_get_agent_recent_logs");
```

- [ ] **步骤 2：注册只读诊断 MCP 工具**

在 `PoeMcpTools.RegisterAll` 增加：

```csharp
registry.Register(
    new McpToolDefinition(
        "poe_get_agent_run_trace",
        "Read a POE Studio Agent run trace by runId. Use this to diagnose why a prior chat/tool run failed or produced no final answer.",
        ObjectSchema(("runId", "string")),
        ReadOnlyAnnotations),
    (arguments, cancellationToken) => GetAgentRunTraceAsync(workspace, arguments, cancellationToken));

registry.Register(
    new McpToolDefinition(
        "poe_get_agent_recent_logs",
        "Read recent POE Studio API/MCP log summaries for diagnosing agent bridge failures. Returns bounded text only.",
        ObjectSchema(("maxLines", "integer")),
        ReadOnlyAnnotations),
    (arguments, cancellationToken) => GetAgentRecentLogsAsync(workspace, arguments, cancellationToken));
```

- [ ] **步骤 3：实现 trace 工具**

`GetAgentRunTraceAsync` 从 `AgentRunTraceStore` 读取 runId，返回最多 200 条事件：

```csharp
return JsonSuccess(new
{
    runId,
    events = events.TakeLast(200)
});
```

- [ ] **步骤 4：实现日志摘要工具**

只允许读取 workspace root 下这些日志：

```csharp
poe-studio-dev.out.log
poe-studio-dev.err.log
poe-current-view-acceptance.out.log
poe-current-view-acceptance.err.log
```

返回最后 `maxLines` 行，默认 80，最大 300。不得读取任意路径。

实现要求：

```csharp
var maxLines = Math.Clamp(GetInt32(arguments, "maxLines") ?? 80, 1, 300);
var allowedNames = new[]
{
    "poe-studio-dev.out.log",
    "poe-studio-dev.err.log",
    "poe-current-view-acceptance.out.log",
    "poe-current-view-acceptance.err.log"
};

var entries = new List<object>();
foreach (var name in allowedNames)
{
    var path = Path.Combine(workspaceRoot, name);
    var fullPath = Path.GetFullPath(path);
    if (!fullPath.StartsWith(Path.GetFullPath(workspaceRoot), StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    if (!File.Exists(fullPath))
    {
        entries.Add(new { name, exists = false, lines = Array.Empty<string>() });
        continue;
    }

    var lines = File.ReadLines(fullPath).TakeLast(maxLines).ToArray();
    entries.Add(new { name, exists = true, lines });
}

return JsonSuccess(new { maxLines, entries });
```

禁止从 MCP 参数中接收文件名、路径或 glob。测试必须覆盖 `maxLines=-1`、`maxLines=9999` 被 clamp。

- [ ] **步骤 5：ChatService 自动启动诊断 run**

当 `finding.ShouldStartDiagnosticRun` 为 true，追加第二次 Codex 调用。诊断 run 必须使用 `runMode = AgentRunModes.Diagnostic`，并且不得再次触发自动诊断：

```csharp
var diagnosticRunId = Guid.NewGuid().ToString("N");
await _traceStore.AppendAsync(diagnosticRunId, new AgentRunTraceEventDto("run", "started", JsonSerializer.Serialize(new { runMode = AgentRunModes.Diagnostic, sourceRunId = runId, finding.Code }), DateTimeOffset.UtcNow), CancellationToken.None);
var diagnosticPrompt = BuildDiagnosticPrompt(runId, finding);
var diagnosticResult = await _runner.RunAsync(settings, diagnosticPrompt, async parsedEvent =>
{
    foreach (var sseEvent in ConvertToSseEvents(parsedEvent))
        await channel.Writer.WriteAsync(sseEvent, CancellationToken.None);
}, CancellationToken.None);
```

`BuildDiagnosticPrompt` 必须包含：

```text
You are diagnosing a failed POE Studio Agent run.
Original runId: {runId}
Finding: {finding.Code}
Use poe_get_agent_run_trace first.
Use poe_get_agent_recent_logs if trace is insufficient.
Do not modify files in diagnostic mode.
Return:
1. root cause
2. evidence
3. whether the original user task can continue
4. whether code repair approval is required
```

- [ ] **步骤 6：运行测试**

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "ChatServiceIntegrationTests|McpToolRegistryTests|McpPoeToolsTests"
```

预期：PASS。

- [ ] **步骤 7：Commit**

```powershell
git add src/PoeStudio.Api/ChatService.cs src/PoeStudio.Mcp/PoeMcpTools.cs src/PoeStudio.Mcp/McpProtocol.cs tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs tests/PoeStudio.Tests/McpToolRegistryTests.cs tests/PoeStudio.Tests/McpPoeToolsTests.cs
git commit -m "feat(agent): add automatic diagnostic runs"
```

---

## 任务 5：前端展示诊断与修复审批

**文件：**
- 修改：`src/PoeStudio.Api/wwwroot/index.html`
- 修改：`src/PoeStudio.Api/wwwroot/app.js`
- 修改：`src/PoeStudio.Api/wwwroot/styles.css`
- 测试：`tests/PoeStudio.Tests/FrontendDatc64WorkflowTests.cs`

- [ ] **步骤 1：写前端静态测试**

在 `FrontendDatc64WorkflowTests.cs` 增加：

```csharp
[Fact]
public void Chat_ui_exposes_diagnostic_and_repair_approval_controls()
{
    var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var html = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "index.html"));
    var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));

    Assert.Contains("agentDiagnosticPanel", html);
    Assert.Contains("approveAgentRepairBtn", html);
    Assert.Contains("case \"diagnostic\":", appJs);
    Assert.Contains("renderAgentDiagnostic", appJs);
    Assert.Contains("approveAgentRepair", appJs);
}
```

- [ ] **步骤 2：HTML 增加诊断面板**

在 chat workspace 内加入：

```html
<section id="agentDiagnosticPanel" class="agent-diagnostic-panel hidden">
  <div id="agentDiagnosticSummary"></div>
  <button id="approveAgentRepairBtn" type="button" disabled>批准 Agent 修复</button>
</section>
```

- [ ] **步骤 3：JS 处理 diagnostic SSE**

在 `handleChatSseEvent`：

```javascript
case "diagnostic":
  renderAgentDiagnostic(data.finding || data);
  break;
```

新增：

```javascript
function renderAgentDiagnostic(finding) {
  const panel = $("agentDiagnosticPanel");
  panel.classList.remove("hidden");
  $("agentDiagnosticSummary").textContent = `${finding.code || "diagnostic"}：${finding.summary || ""}`;
  $("approveAgentRepairBtn").disabled = finding.severity !== "high";
  state.chat.pendingRepair = finding;
  releaseChatSendLock();
}
```

- [ ] **步骤 4：批准按钮绑定**

```javascript
$("approveAgentRepairBtn").addEventListener("click", approveAgentRepair);

async function approveAgentRepair() {
  if (!state.chat.pendingRepair) return;
  releaseChatSendLock();
  await api("/api/agent/repair/approve", {
    runId: state.chat.pendingRepair.runId,
    code: state.chat.pendingRepair.code
  });
}
```

同时在 `handleChatSseEvent` 的 `done` 分支中识别 `autoDiagnostic`：

```javascript
case "done":
  updateLastChatMessage("status", "");
  releaseChatSendLock();
  if (data.autoDiagnostic) {
    addChatMessage("status", "检测到本次运行未完成，正在启动自动诊断...");
  }
  break;
```

- [ ] **步骤 5：运行测试**

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~FrontendDatc64WorkflowTests.Chat_ui_exposes_diagnostic_and_repair_approval_controls"
```

预期：PASS。

- [ ] **步骤 6：Commit**

```powershell
git add src/PoeStudio.Api/wwwroot/index.html src/PoeStudio.Api/wwwroot/app.js src/PoeStudio.Api/wwwroot/styles.css tests/PoeStudio.Tests/FrontendDatc64WorkflowTests.cs
git commit -m "feat(agent): show diagnostics and repair approval in chat"
```

---

## 任务 6：用户批准后的 repair run

**文件：**
- 创建：`src/PoeStudio.Api/AgentRepairService.cs`
- 修改：`src/PoeStudio.Api/Program.cs`
- 修改：`src/PoeStudio.Api/ChatService.cs`
- 测试：`tests/PoeStudio.Tests/AgentRepairServiceTests.cs`
- 测试：`tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs`

- [ ] **步骤 1：编写失败测试：未批准不能修复**

`AgentRepairServiceTests.cs`：

```csharp
namespace PoeStudio.Tests;

public sealed class AgentRepairServiceTests
{
    [Fact]
    public async Task StartRepairAsync_rejects_missing_user_approval()
    {
        var service = AgentRepairServiceTestsFactory.Create();

        var result = await service.StartRepairAsync("abc", "no_final_answer_after_tool_result", userApproved: false, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("approval", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **步骤 2：实现 Repair DTO**

```csharp
public sealed record AgentRepairStartResultDto(
    bool Accepted,
    string Message,
    string? RepairRunId);
```

- [ ] **步骤 3：实现 AgentRepairService**

职责：

```csharp
public async Task<AgentRepairStartResultDto> StartRepairAsync(
    string runId,
    string diagnosticCode,
    bool userApproved,
    CancellationToken cancellationToken)
```

未批准直接返回 `Accepted=false`。

批准后：

```csharp
var gitStatus = await RunProcessCaptureAsync("git", "status --short --branch", repositoryRoot, cancellationToken);
var prompt = BuildRepairPrompt(runId, diagnosticCode, gitStatus);
await _runner.RunAsync(repairSettings, prompt, onEvent, cancellationToken);
```

`repairSettings` 必须：

```csharp
Sandbox: "workspace-write",
ApprovalMode: "never",
WorkingDirectory: repositoryRoot,
Memories: false,
Skills: false,
CommandExecution: true
```

只有用户点击批准后才允许这一配置。普通 `normal` 和 `diagnostic` run 必须继续保持：

```csharp
Memories: false,
Skills: false,
CommandExecution: false
```

如果当前 `AgentSettingsDto` 尚无这些字段，必须先在 `src/PoeStudio.Core/Agent/AgentSettingsDto.cs` 增加：

```csharp
bool Memories = false,
bool Skills = false,
bool CommandExecution = false
```

并在 `CodexProcessRunner.BuildStartInfo` 中把这些字段映射到 Codex CLI 配置。测试必须覆盖 repair run 会打开 command execution，normal/diagnostic run 不会打开。

- [ ] **步骤 4：repair prompt 必须包含固定流程**

```text
The user approved code repair for POE Studio Agent run {runId}.
You may inspect and edit project files inside repository root only.
Run mode: repair.
Codex capabilities for this run: memories disabled, skills disabled, command execution enabled, workspace-write sandbox.
Before editing:
1. inspect run trace with poe_get_agent_run_trace
2. inspect relevant code
3. state the root cause in the chat
Then:
1. write failing test
2. confirm it fails
3. implement minimal fix
4. run targeted tests
5. run broader regression tests
6. restart POE Studio if needed
7. re-run or instruct user to re-run the original task
Do not modify unrelated files.
Do not use destructive git commands.
```

- [ ] **步骤 5：新增 API**

`Program.cs`：

```csharp
app.MapPost("/api/agent/repair/approve", async (
    AgentRepairApproveRequest request,
    AgentRepairService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.StartRepairAsync(request.RunId, request.Code, userApproved: true, cancellationToken);
    return Results.Ok(ApiResponse<AgentRepairStartResultDto>.Success(result));
});
```

- [ ] **步骤 6：运行测试**

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "AgentRepairServiceTests|ChatServiceIntegrationTests"
```

预期：PASS。

- [ ] **步骤 7：Commit**

```powershell
git add src/PoeStudio.Api/AgentRepairService.cs src/PoeStudio.Api/Program.cs tests/PoeStudio.Tests/AgentRepairServiceTests.cs tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs
git commit -m "feat(agent): add approved code repair runs"
```

---

## 任务 7：实机验收脚本与回归场景

**文件：**
- 创建：`docs/superpowers/reports/2026-05-25-agent-self-diagnosis-repair-loop-acceptance.md`
- 修改：`docs/agent/poe-studio-agent-context.md`

- [ ] **步骤 1：记录验收场景**

验收报告必须包含以下 4 个实机场景：

```markdown
## 场景 1：正常 current-view 漏翻查询
- 用户输入：列出当前表格 3 个漏翻内容给我
- 预期工具：poe_get_current_view_context、poe_find_current_table_untranslated_cells
- 禁止工具：poe_datc64_extract_translatable_cells
- 预期结果：AI 输出 3 条候选或明确说明不足 3 条

## 场景 2：工具 completed 但无最终回答
- 注入 fake Codex events：tool completed + done，无 message
- 预期：自动 diagnostic 事件出现
- 预期：诊断说明 no_final_answer_after_tool_result

## 场景 3：工具 in_progress 悬挂
- 注入 fake Codex events：tool in_progress 后无 completed
- 小于 30 秒：不得触发诊断
- 超过 30 秒：触发诊断
- 预期：自动 diagnostic 事件出现
- 预期：诊断说明 tool_call_left_in_progress

## 场景 4：用户批准 repair
- 用户点击批准 Agent 修复
- 预期：repair run 记录 git status
- 预期：repair run 先写失败测试，再修复，再跑测试
```

- [ ] **步骤 2：更新 agent-context 文档**

在 `docs/agent/poe-studio-agent-context.md` 增加“异常路径规则”：

```markdown
当工具调用失败、超时、无最终回答或 UI 卡住时，Agent 必须先读取 run trace 和日志摘要，自行判断断点；普通诊断不得修改代码。只有用户明确批准 repair run 后，Agent 才能修改项目代码，并且必须先写失败测试、再最小修复、再验证。
```

- [ ] **步骤 3：全量验证**

```powershell
dotnet test PoeStudio.sln --no-restore
```

预期：全部通过。

- [ ] **步骤 4：GitNexus 变更检查**

```powershell
npx gitnexus analyze
```

然后运行 GitNexus `detect_changes(scope="all")`，验收报告记录：

```markdown
GitNexus detect_changes:
- risk_level:
- changed_count:
- affected_processes:
```

- [ ] **步骤 5：Commit**

```powershell
git add docs/superpowers/reports/2026-05-25-agent-self-diagnosis-repair-loop-acceptance.md docs/agent/poe-studio-agent-context.md
git commit -m "docs(agent): record self diagnosis repair loop acceptance"
```

---

## 3. 执行顺序与验收口径

- [ ] 先完成任务 1。任务 1 不通过，不允许推进自诊断，因为工具结果都没闭环。
- [ ] 再完成任务 2。没有 run trace，不允许声称 Agent 能自查。
- [ ] 任务 3 必须同时覆盖 no-final-answer、tool-hang 30 秒阈值、diagnostic/repair 不递归。
- [ ] 任务 4 合格标准是诊断 run 能读取上一 run trace，并且日志工具无路径输入。
- [ ] 任务 5 只是 UI 可见化，不算核心能力。
- [ ] 任务 6 必须证明 repair run 的 Codex 能力配置和 normal/diagnostic 不同，否则不算“用户批准后能自修复”。
- [ ] 任务 7 是最终验收，不允许只用单元测试替代实机。

---

## 4. 最终验收命令

```powershell
dotnet build PoeStudio.sln --no-restore
dotnet test PoeStudio.sln --no-restore
dotnet run --project src/PoeStudio.Api/PoeStudio.Api.csproj --no-build --urls http://localhost:5010
```

实机输入：

```text
列出当前表格 3 个漏翻内容给我
```

必须观察到：

- `poe_get_current_view_context` completed
- `poe_find_current_table_untranslated_cells` completed
- UI 显示工具结果摘要
- AI 输出 3 条候选或不足 3 条的明确原因
- 无 `poe_datc64_extract_translatable_cells`
- 无 Oodle 错误
- 新消息可继续发送

异常注入验收：

- fake tool completed + no final answer -> 自动诊断
- fake tool in_progress < 30s -> 不自动诊断
- fake tool in_progress > 30s -> 自动诊断
- diagnostic run in_progress > 30s -> 不递归诊断，只显示诊断失败
- 用户批准 repair -> repair run 记录 git status、写测试、修复、跑测试
