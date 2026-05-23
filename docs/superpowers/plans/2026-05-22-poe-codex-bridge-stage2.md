# POE Studio Codex Bridge Stage 2 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 在 POE Studio 后端接入 Codex 运行时，形成可持久化、可审计、带审批门禁的通用 Agent 会话后端，并用 DATC64 翻译候选生成到人工批准写入 overlay draft 作为第一个受控能力闭环。

**架构：** Stage 2 只做后端 Agent runtime，不做正式 Agent Workspace UI。后端以 `codex exec --json` 子进程作为稳定桥接路径，通过通用 `AgentOrchestrator` 创建 thread/message/run/plan，使用 `AgentPromptBuilder` 把用户目标、项目上下文、Stage 1 MCP 工具约束和能力清单组装为 Codex 任务，读取 JSONL 事件并持久化 thread/message/run/event/plan/approval/tool-call；Codex 通过 Stage 1 `poe-studio` MCP 工具感知项目。`Core` 只放能力定义、prompt、runner、parser，不引用 `Storage`；需要 `AgentStore` 的编排器放在 `Storage`。所有写入 overlay draft 的动作必须等待 POE Studio 后端审批记录进入 `approved` 状态后才执行。

**技术栈：** .NET 8、ASP.NET Core Minimal API、xUnit、System.Text.Json、Codex CLI `exec --json`、POE Studio Stage 1 MCP Tools、POE Studio Core/Storage/Contracts、OverlayStore、TableInspector。

---

## 0. Stage 2 硬约束

- [ ] **S2-H0.1：Stage 1 必须先合入或作为执行基线**  
  执行 Stage 2 前，当前工作树必须包含 Stage 1 的 `src/PoeStudio.Mcp`、MCP 测试、验收报告，且 `docs/superpowers/reports/2026-05-22-poe-mcp-stage1-acceptance.md` 写明 `Stage 1 status: PASS`。如果当前 `main` 尚未合入 `codex/poe-mcp-stage1`，先停止并完成 Stage 1 合入计划，不得在缺少 Stage 1 代码的 main 上实现 Stage 2。

- [ ] **S2-H0.2：本阶段仍不做正式 UI**  
  Stage 2 允许新增后端 API、后端存储、服务和 API smoke tests。禁止新增 Agent Workspace 正式前端视图、聊天界面、事件时间线 UI、审批面板 UI。这些属于 Stage 3。

- [ ] **S2-H0.3：不是无状态命令包装器**  
  后端不得把 `codex exec` 包成一次性按钮。每次 Agent run 必须有持久化 thread、run、events、plan、approval、status、result 或 failure。

- [ ] **S2-H0.4：写入必须审批**  
  Codex 只能生成候选和 draft proposal。任何 overlay 写入必须由 POE Studio 后端在 `approval.status == approved` 后执行。未经批准时，不能调用 `OverlayStore.SaveTextAsync`、`TableInspector.ApplyCellEditsToBytes` 或任何写文件 API。

- [ ] **S2-H0.5：Codex app-server 不作为 Stage 2 主路径**  
  当前 `codex app-server` 仍是 experimental。Stage 2 主路径使用 `codex exec --json` 子进程。app-server 只能写入调查记录，不得作为 Stage 2 验收阻塞项。

- [ ] **S2-H0.6：模型配置必须持久化**  
  后端必须保存 Codex model/profile/sandbox/cwd/MCP server name 等配置，刷新页面或重启服务后仍能读取。禁止每次请求重新手填模型配置。

- [ ] **S2-H0.7：失败必须可审计**  
  Codex 子进程退出码、stderr 摘要、JSONL 事件、最后状态和失败原因必须写入 run events。失败不能只返回 500 或“需要添加索引”。

- [ ] **S2-H0.8：权限和写入范围最小化**  
  Stage 2 只允许 DATC64 翻译 draft 写入 overlay。禁止新增任意 shell 工具、任意文件写入工具、自动工具生成器、项目代码自修改工具。

- [ ] **S2-H0.9：不得破坏现有项目分层**  
  当前引用方向是 `Api -> Storage -> Core -> Contracts`。Stage 2 不得让 `Core` 反向引用 `Storage`。Codex 运行、JSONL 解析、DATC64 proposal 解析、prompt 构建可以放 `Core`，但不得直接依赖 `AgentStore`；涉及 `AgentStore`、`OverlayStore` 的 orchestrator 和 draft 写入服务必须放 `Storage`，或由 `Api` 编排调用 `Storage` 和 `Core`。

- [ ] **S2-H0.10：项目记忆门禁必须真实执行**  
  执行 Stage 2 前必须读取 `docs/ai-project-memory.md`。如果当前工作树缺少该文件，执行者必须在 Stage 2 执行记录中写明 `docs/ai-project-memory.md missing` 并停止，先恢复项目记忆文件或取得明确批准后再继续。禁止在未读取项目记忆的情况下声明“已按项目规则执行”。

- [ ] **S2-H0.11：必须交付通用 Agent 后端骨架，而不是 DATC64 专用流程**  
  DATC64 只是 Stage 2 的第一个写入型能力。Stage 2 必须同时支持 `question`、`read-only-analysis`、`datc64-translation` 三类 `taskKind`，并通过统一 thread/message/run/plan/event/approval 存储、统一 prompt 构建、统一 Codex runner 处理。禁止把 `/api/agent/runs` 写成只认识 DATC64 的专用接口。

- [ ] **S2-H0.12：只读任务也必须真实走 Agent 循环**  
  `question` 和 `read-only-analysis` 不允许直接返回固定文本或后端硬编码答案，必须创建 run、生成 plan、启动 Codex、记录 MCP/tool/agent events、保存 final result。它们没有写入权限，不能产生 overlay approval。

- [ ] **S2-H0.13：每个 run 必须有可恢复的计划和消息上下文**  
  Stage 2 必须持久化用户消息、Agent 消息、plan steps、run attempt。服务重启后能读取 thread 历史、latest plan、events、approval 状态和失败原因。禁止只保存一条 run summary。

---

## 1. 文件结构

### 新增文件

- `src/PoeStudio.Contracts/AgentDtos.cs`  
  Stage 2 API 契约：thread、message、run、event、plan、approval、capability、settings、DATC64 draft proposal DTO。

- `src/PoeStudio.Core/Agent/AgentCapabilities.cs`  
  定义 Stage 2 能力注册表：`question`、`read-only-analysis`、`datc64-translation`。每个能力声明是否允许写入、需要哪些 MCP 工具、是否需要 approval、输出 schema 名称。

- `src/PoeStudio.Core/Agent/AgentPromptBuilder.cs`  
  根据 thread 历史、用户目标、taskKind、resourcePath、能力约束、MCP server name 生成 Codex prompt。这里固定“先计划、再调用工具、最后给结构化结果”的 Agent 行为，不把 prompt 散落在 API handler 中。

- `src/PoeStudio.Storage/Agent/AgentStore.cs`  
  JSON 文件持久化存储，根目录为 workspace 级 `agent` 目录。负责 thread/message/run/event/plan/approval/settings 的读写。

- `src/PoeStudio.Storage/Agent/AgentOrchestrator.cs`  
  通用运行编排：创建 user message、创建 run、初始化 plan、调用 `CodexProcessRunner`、解析 final result、根据 taskKind 创建 approval 或保存只读结果。它需要 `AgentStore`，所以必须放在 `Storage`。API 只调用 orchestrator，不直接拼 Codex 命令。

- `src/PoeStudio.Core/Agent/AgentModels.cs`  
  后端内部模型和枚举：run status、event type、approval status、plan step status。

- `src/PoeStudio.Core/Agent/CodexJsonEventParser.cs`  
  解析 `codex exec --json` JSONL 事件，提取 agent message、mcp tool call、command execution、error、final message。

- `src/PoeStudio.Core/Agent/CodexProcessRunner.cs`  
  用 `ProcessStartInfo.ArgumentList` 启动 `codex exec --json`，读取 stdout/stderr，通过回调或 `IAsyncEnumerable<CodexParsedEvent>` 返回事件。禁止该文件直接引用 `AgentStore`。

- `src/PoeStudio.Core/Agent/Datc64TranslationDraftParser.cs`  
  从 Codex 最终结构化输出中解析 DATC64 翻译候选和 cell locator。

- `src/PoeStudio.Storage/Agent/Datc64DraftApplyService.cs`  
  在审批通过后，将候选翻译转换为 `TableCellEditDto`，调用 `TableInspector.ApplyCellEditsToBytes` 并通过 `OverlayStore.SaveBytesAsync` 写 overlay draft。该文件放在 `Storage`，因为它需要依赖 `OverlayStore`；禁止把它放进 `Core` 后再让 `Core` 引用 `Storage`。

- `src/PoeStudio.Api/AgentRoutes.cs`  
  Stage 2 Minimal API 路由扩展，避免继续膨胀 `Program.cs`。

- `tests/PoeStudio.Tests/AgentStoreTests.cs`  
  存储持久化、重启后可恢复、message/plan/event/approval 状态转移测试。

- `tests/PoeStudio.Tests/AgentCapabilitiesTests.cs`  
  能力注册表测试：三类 taskKind 存在，DATC64 需要 approval，question/read-only-analysis 不允许写入。

- `tests/PoeStudio.Tests/AgentPromptBuilderTests.cs`  
  prompt 构建测试：包含用户目标、MCP server name、允许工具、禁止写入边界、输出格式、历史消息摘要。

- `tests/PoeStudio.Tests/AgentOrchestratorTests.cs`  
  编排测试：question/read-only-analysis/datc64-translation 都通过统一 orchestrator 创建 run、plan、events 和结果。

- `tests/PoeStudio.Tests/CodexJsonEventParserTests.cs`  
  JSONL 事件解析测试。

- `tests/PoeStudio.Tests/CodexProcessRunnerTests.cs`  
  fake codex executable 测试子进程事件采集、失败记录、超时取消。

- `tests/PoeStudio.Tests/Datc64TranslationDraftParserTests.cs`  
  翻译候选 JSON 解析和 locator 校验测试。

- `tests/PoeStudio.Tests/Datc64DraftApplyServiceTests.cs`  
  审批后写 overlay draft、未审批不写、locator 不匹配失败测试。

- `tests/PoeStudio.Tests/AgentApiSmokeTests.cs`  
  Stage 2 后端 API smoke tests。

- `docs/superpowers/reports/2026-05-22-poe-codex-bridge-stage2-acceptance.md`  
  Stage 2 验收报告。

### 修改文件

- `src/PoeStudio.Api/Program.cs`  
  只允许增加 DI 注册和 `app.MapAgentRoutes()`。新增路由实现必须放 `AgentRoutes.cs`。

- `src/PoeStudio.Api/PoeStudio.Api.csproj`  
  如需显式包含新增文件，按 SDK 默认规则可不修改。

- `src/PoeStudio.Core/PoeStudio.Core.csproj`  
  不得新增对 `PoeStudio.Storage` 的引用。Stage 2 默认不新增第三方依赖。

- `src/PoeStudio.Storage/PoeStudio.Storage.csproj`  
  可继续引用 `Contracts` 和 `Core`。Stage 2 默认不新增第三方依赖。

- `tests/PoeStudio.Tests/PoeStudio.Tests.csproj`  
  如需 fake executable 输出文件复制到测试目录时修改。

---

## 2. Stage 2 API 契约

### 必须新增路由

- [ ] `GET /api/agent/settings`  
  返回 Codex bridge 设置：`codexPath`、`model`、`profile`、`sandbox`、`mcpServerName`、`workingDirectory`、`approvalMode`。

- [ ] `POST /api/agent/settings`  
  保存设置。必须验证 `codexPath` 可执行或为 `codex`，`mcpServerName` 默认 `poe-studio`，`sandbox` 只能为 `read-only`、`workspace-write`、`danger-full-access`。

- [ ] `POST /api/agent/threads`  
  创建 thread。输入 `profileId`、`title`、`goal`、`taskKind`。返回 thread snapshot。

- [ ] `POST /api/agent/threads/{threadId}/messages`  
  向 thread 追加用户消息。输入 `content`、`attachments` 可为空。返回 message snapshot。消息必须持久化，不能只作为启动 run 的临时 prompt。

- [ ] `GET /api/agent/threads/{threadId}`  
  返回 thread、messages、最近 runs、latest plan、pending approvals。

- [ ] `POST /api/agent/runs`  
  创建并启动 run。输入 `threadId`、`profileId`、`goal`、`taskKind`、`resourcePath` 可选。返回 run snapshot。Stage 2 必须支持 `question`、`read-only-analysis`、`datc64-translation`。只有 `taskKind = "datc64-translation"` 可以生成写入型候选 workflow；其他任务只读。

- [ ] `POST /api/agent/runs/{runId}/retry`  
  基于同一 thread、同一 taskKind 和最后用户目标创建新 attempt。必须保留旧 run events，不得覆盖历史。

- [ ] `GET /api/agent/runs/{runId}`  
  返回 run 当前状态、plan、event count、approval count、result summary。

- [ ] `GET /api/agent/runs/{runId}/events`  
  返回 run events，支持 `afterSequence` 增量拉取。

- [ ] `POST /api/agent/runs/{runId}/cancel`  
  请求取消运行中子进程，并记录 cancellation event。

- [ ] `POST /api/agent/approvals/{approvalId}/approve`  
  批准 pending approval。对于 DATC64 draft approval，批准后才调用 apply service 写 overlay。

- [ ] `POST /api/agent/approvals/{approvalId}/reject`  
  拒绝 pending approval，不写 overlay，run 进入 `Rejected` 或 `CompletedWaitingForUser`。

### Stage 2 禁止路由

- [ ] 禁止新增正式 Agent Workspace 页面路由。
- [ ] 禁止新增能直接执行任意 shell 的 `/api/agent/tools/run-command`。
- [ ] 禁止新增未审批直接写 overlay 的 `/api/agent/apply`。
- [ ] 禁止新增只服务 DATC64 的 `/api/agent/datc64/*` 作为主入口；DATC64 必须走统一 `/api/agent/runs`。

---

## 3. 数据模型

### AgentDtos.cs 必须包含

```csharp
namespace PoeStudio.Contracts;

public enum AgentThreadStatus { Active = 0, Archived = 1 }
public enum AgentRunStatus { Queued = 0, Running = 1, WaitingForApproval = 2, Succeeded = 3, Failed = 4, Cancelled = 5, Rejected = 6 }
public enum AgentEventType { RunCreated = 0, PlanUpdated = 1, CodexStdout = 2, CodexStderr = 3, McpToolCall = 4, AgentMessage = 5, ApprovalRequested = 6, ApprovalApproved = 7, ApprovalRejected = 8, OverlayDraftWritten = 9, RunFailed = 10, RunCancelled = 11 }
public enum AgentApprovalStatus { Pending = 0, Approved = 1, Rejected = 2, Applied = 3 }
public enum AgentMessageRole { User = 0, Assistant = 1, System = 2 }
public enum AgentCapabilityKind { ReadOnly = 0, WriteWithApproval = 1 }

public sealed record AgentSettingsDto(
    string CodexPath,
    string? Model,
    string? Profile,
    string Sandbox,
    string McpServerName,
    string WorkingDirectory,
    string ApprovalMode);

public sealed record AgentThreadDto(
    string Id,
    string ProfileId,
    string Title,
    string Goal,
    string TaskKind,
    AgentThreadStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AgentMessageDto(
    string Id,
    string ThreadId,
    AgentMessageRole Role,
    string Content,
    string? PayloadJson,
    DateTimeOffset CreatedAt);

public sealed record AgentRunDto(
    string Id,
    string ThreadId,
    string ProfileId,
    string Goal,
    string TaskKind,
    AgentRunStatus Status,
    int ProgressPercent,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int EventCount,
    string? ErrorCode,
    string? ErrorMessage,
    string? ResultJson);

public sealed record AgentEventDto(
    string Id,
    string RunId,
    long Sequence,
    AgentEventType Type,
    string Message,
    string? PayloadJson,
    DateTimeOffset CreatedAt);

public sealed record AgentPlanStepDto(
    string Id,
    string RunId,
    int Order,
    string Title,
    string Status,
    string? Evidence);

public sealed record AgentCapabilityDto(
    string TaskKind,
    string DisplayName,
    AgentCapabilityKind Kind,
    IReadOnlyList<string> RequiredMcpTools,
    bool RequiresApproval,
    string OutputSchemaName);

public sealed record AgentApprovalDto(
    string Id,
    string RunId,
    string ProfileId,
    string Kind,
    AgentApprovalStatus Status,
    string Summary,
    string ProposalJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? AppliedOverlayPath);
```

---

## 4. 任务分解

### 任务 1：Stage 2 前置门禁和路由影响分析

**文件：**
- 读取：`docs/ai-project-memory.md`
- 读取：`docs/superpowers/reports/2026-05-22-poe-mcp-stage1-acceptance.md`
- 读取：`docs/superpowers/plans/2026-05-22-poe-mcp-stage1.md`
- 修改：无

- [ ] **步骤 1：确认项目记忆文件存在并已读取**

运行：

```powershell
Test-Path docs\ai-project-memory.md
Get-Content docs\ai-project-memory.md -TotalCount 240
```

预期：第一条返回 `True`，第二条能读取项目目的、架构、工作流、高风险区、测试地图和 AI 工作约定。如果 `Test-Path` 返回 `False`，停止 Stage 2，在执行记录中写 `docs/ai-project-memory.md missing`。

- [ ] **步骤 2：确认 Stage 1 已在当前工作树**

运行：

```powershell
Test-Path src\PoeStudio.Mcp\PoeStudio.Mcp.csproj
Test-Path docs\superpowers\reports\2026-05-22-poe-mcp-stage1-acceptance.md
Select-String -Path docs\superpowers\reports\2026-05-22-poe-mcp-stage1-acceptance.md -Pattern "Stage 1 status: PASS"
```

预期：三项都成功。如果失败，停止 Stage 2。

- [ ] **步骤 3：运行 GitNexus route impact 预检查**

新增 API route 前必须通过 GitNexus MCP 工具运行影响分析，不是 PowerShell 命令：

```text
mcp__gitnexus__impact({
  "repo": "POE-Studio",
  "target": "Program",
  "direction": "upstream"
})
```

记录风险等级。若 HIGH 或 CRITICAL，先把 `AgentRoutes.cs` 独立扩展策略写入执行记录，再继续。

- [ ] **步骤 4：确认 Codex CLI 能用**

运行：

```powershell
codex --version
codex exec --help
codex mcp get poe-studio
```

预期：能看到 Codex 版本、`--json` 选项和 `poe-studio` MCP 配置。

- [ ] **步骤 5：Commit**

本任务不修改文件，不需要 commit。执行记录写入 Stage 2 执行日志。

### 任务 2：定义 Agent API DTO 和能力契约

**文件：**
- 创建：`src/PoeStudio.Contracts/AgentDtos.cs`
- 创建：`src/PoeStudio.Core/Agent/AgentCapabilities.cs`
- 测试：`tests/PoeStudio.Tests/AgentStoreTests.cs`
- 测试：`tests/PoeStudio.Tests/AgentCapabilitiesTests.cs`

- [ ] **步骤 1：写 DTO 编译测试**

在 `AgentStoreTests.cs` 添加测试，先引用 `AgentSettingsDto`、`AgentThreadDto`、`AgentMessageDto`、`AgentRunDto`、`AgentEventDto`、`AgentApprovalDto`、`AgentCapabilityDto`，断言 JSON 序列化包含字段名 `threadId`、`approvalMode`、`taskKind`。

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentStoreTests
```

预期：FAIL，类型不存在。

- [ ] **步骤 2：写能力注册表测试**

在 `AgentCapabilitiesTests.cs` 添加测试：
- `question` 存在，`Kind == ReadOnly`，`RequiresApproval == false`。
- `read-only-analysis` 存在，`Kind == ReadOnly`，`RequiresApproval == false`，需要至少一个 `poe_*` MCP 读取工具。
- `datc64-translation` 存在，`Kind == WriteWithApproval`，`RequiresApproval == true`，必须包含 `poe_datc64_extract_translatable_cells`。

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentCapabilitiesTests
```

预期：FAIL，`AgentCapabilities` 不存在。

- [ ] **步骤 3：创建 AgentDtos.cs**

按本计划第 3 节的代码创建 DTO。不要把 DTO 放入 `Program.cs`。

- [ ] **步骤 4：创建 AgentCapabilities.cs**

实现静态注册表：
- `AgentCapabilities.All`
- `AgentCapabilities.GetRequired(string taskKind)`
- 未知 `taskKind` 返回 `ArgumentException("unsupported_task_kind")`

不得把 DATC64 判断散落在 API handler 中。

- [ ] **步骤 5：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentStoreTests|FullyQualifiedName~AgentCapabilitiesTests"
```

预期：PASS。

- [ ] **步骤 6：Commit**

```powershell
git add src\PoeStudio.Contracts\AgentDtos.cs src\PoeStudio.Core\Agent\AgentCapabilities.cs tests\PoeStudio.Tests\AgentStoreTests.cs tests\PoeStudio.Tests\AgentCapabilitiesTests.cs
git commit -m "feat(agent): define Stage 2 agent contracts and capabilities"
```

### 任务 3：实现 AgentStore 持久化

**文件：**
- 创建：`src/PoeStudio.Storage/Agent/AgentStore.cs`
- 修改：`tests/PoeStudio.Tests/AgentStoreTests.cs`

- [ ] **步骤 1：写存储失败测试**

覆盖：
- 保存 settings 后重新 new store 能读取。
- 创建 thread 后重新 new store 能读取。
- 追加 user message 和 assistant message 后重新 new store 能按时间读取。
- 创建 run 后 append event，sequence 从 1 自增。
- 保存 plan steps 后重新 new store 能读取 latest plan。
- pending approval 审批后状态变 `Approved`，重复 approve 返回失败。

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentStoreTests
```

预期：FAIL，`AgentStore` 不存在。

- [ ] **步骤 2：实现 AgentStore**

要求：
- 构造函数 `AgentStore(string workspaceRoot)`。
- settings 路径：`Path.Combine(workspaceRoot, "agent", "settings.json")`。
- threads 路径：`Path.Combine(workspaceRoot, "agent", "threads", threadId, "thread.json")`。
- messages 路径：`Path.Combine(workspaceRoot, "agent", "threads", threadId, "messages.jsonl")`。
- runs 路径：`Path.Combine(workspaceRoot, "agent", "threads", threadId, "runs", runId, "run.json")`。
- plan 路径：`Path.Combine(workspaceRoot, "agent", "threads", threadId, "runs", runId, "plan.json")`。
- events 路径：`Path.Combine(workspaceRoot, "agent", "threads", threadId, "runs", runId, "events.jsonl")`。
- approvals 路径：`Path.Combine(workspaceRoot, "agent", "threads", threadId, "runs", runId, "approvals.json")`。
- 所有写入采用 temp file + replace；events 追加 JSONL。

- [ ] **步骤 3：运行存储测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentStoreTests
```

预期：PASS。

- [ ] **步骤 4：Commit**

```powershell
git add src\PoeStudio.Storage\Agent\AgentStore.cs tests\PoeStudio.Tests\AgentStoreTests.cs
git commit -m "feat(agent): persist threads runs events and approvals"
```

### 任务 4：解析 Codex JSONL 事件

**文件：**
- 创建：`src/PoeStudio.Core/Agent/AgentModels.cs`
- 创建：`src/PoeStudio.Core/Agent/CodexJsonEventParser.cs`
- 创建：`tests/PoeStudio.Tests/CodexJsonEventParserTests.cs`

- [ ] **步骤 1：写解析测试**

用真实 Stage 1 验收 JSONL 片段覆盖：
- `mcp_tool_call` 提取 server、tool、arguments、status。
- `agent_message` 提取 text。
- `command_execution` 提取 command、exit_code、status。
- 非法 JSON 返回 `CodexParsedEvent` 类型 `Unknown`，保留 raw line。

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~CodexJsonEventParserTests
```

预期：FAIL。

- [ ] **步骤 2：实现 parser**

`CodexJsonEventParser.ParseLine(string line)` 返回内部 record，字段包含：
- `RawJson`
- `EventType`
- `Message`
- `PayloadJson`
- `IsTerminal`
- `IsToolCall`
- `ToolName`

- [ ] **步骤 3：运行解析测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~CodexJsonEventParserTests
```

预期：PASS。

- [ ] **步骤 4：Commit**

```powershell
git add src\PoeStudio.Core\Agent\AgentModels.cs src\PoeStudio.Core\Agent\CodexJsonEventParser.cs tests\PoeStudio.Tests\CodexJsonEventParserTests.cs
git commit -m "feat(agent): parse Codex JSONL events"
```

### 任务 5：实现 AgentPromptBuilder

**文件：**
- 创建：`src/PoeStudio.Core/Agent/AgentPromptBuilder.cs`
- 创建：`tests/PoeStudio.Tests/AgentPromptBuilderTests.cs`

- [ ] **步骤 1：写 prompt 构建测试**

覆盖：
- `question` prompt 包含用户目标、`poe-studio` MCP server name、允许只读工具、禁止写入。
- `read-only-analysis` prompt 包含 profileId、resourcePath、索引状态检查要求、最终输出普通 JSON result。
- `datc64-translation` prompt 包含 `poe_datc64_extract_translatable_cells`、DATC64 proposal JSON schema、禁止直接写 overlay。
- thread 历史 messages 会进入 prompt 的 `Conversation history` 区块。

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentPromptBuilderTests
```

预期：FAIL。

- [ ] **步骤 2：实现 AgentPromptBuilder**

输入：
- `AgentSettingsDto settings`
- `AgentCapabilityDto capability`
- `AgentThreadDto thread`
- `IReadOnlyList<AgentMessageDto> messages`
- `string goal`
- `string? resourcePath`

输出 Codex prompt 字符串。必须包含固定行为：
1. 先给出计划。
2. 优先调用 `poe-studio` MCP 工具读取上下文。
3. 不允许调用 shell 写项目文件。
4. 写入型能力只能输出 proposal，不能写 overlay。
5. 最终输出 taskKind 对应 JSON fenced block。

- [ ] **步骤 3：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentPromptBuilderTests
```

预期：PASS。

- [ ] **步骤 4：Commit**

```powershell
git add src\PoeStudio.Core\Agent\AgentPromptBuilder.cs tests\PoeStudio.Tests\AgentPromptBuilderTests.cs
git commit -m "feat(agent): build prompts from capabilities and thread context"
```

### 任务 6：实现 CodexProcessRunner

**文件：**
- 创建：`src/PoeStudio.Core/Agent/CodexProcessRunner.cs`
- 创建：`tests/PoeStudio.Tests/CodexProcessRunnerTests.cs`

- [ ] **步骤 1：写 fake codex 测试**

测试使用临时 PowerShell 脚本作为 fake codex，可输出 JSONL：

```powershell
Write-Output '{"type":"item.started","item":{"type":"mcp_tool_call","server":"poe-studio","tool":"poe_list_profiles","status":"in_progress"}}'
Write-Output '{"type":"item.completed","item":{"type":"agent_message","text":"done"}}'
```

覆盖：
- runner 将 stdout 每行转成 AgentEvent。
- stderr 写入 `CodexStderr` event。
- exit code 非 0 时 run failed。
- cancellation token 触发时 kill process 并记录 cancelled。

- [ ] **步骤 2：实现 runner**

要求：
- 使用 `ProcessStartInfo.ArgumentList`，禁止拼接 shell command。
- 默认参数通过 `ProcessStartInfo.ArgumentList` 依次加入：`exec`、`--json`、`-C`、`workingDirectory`、`prompt`。
- 如果 settings 有 model/profile/sandbox，加入 `-m`、`-p`、`-s`。
- 不使用 `--dangerously-bypass-approvals-and-sandbox`。
- stdout/stderr 异步读取。
- `CodexProcessRunner` 返回事件流或通过回调上报事件，不直接引用 `AgentStore`，不负责持久化。持久化由 `PoeStudio.Storage.Agent.AgentOrchestrator` 统一处理。

- [ ] **步骤 3：运行 runner 测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~CodexProcessRunnerTests
```

预期：PASS。

- [ ] **步骤 4：Commit**

```powershell
git add src\PoeStudio.Core\Agent\CodexProcessRunner.cs tests\PoeStudio.Tests\CodexProcessRunnerTests.cs
git commit -m "feat(agent): run Codex exec and capture events"
```

### 任务 7：DATC64 翻译候选输出协议

**文件：**
- 创建：`src\PoeStudio.Core\Agent\Datc64TranslationDraftParser.cs`
- 创建：`tests\PoeStudio.Tests\Datc64TranslationDraftParserTests.cs`

- [ ] **步骤 1：定义 Codex 输出 schema**

Codex prompt 必须要求最终输出 JSON fenced block：

```json
{
  "taskKind": "datc64-translation",
  "profileId": "profile-id",
  "resourcePath": "metadata/example.datc64",
  "candidates": [
    {
      "locator": "row:1;column:3;name:text_3 @12",
      "rowIndex": 0,
      "columnIndex": 3,
      "sourceText": "NoMana",
      "translatedText": "法力不足",
      "confidence": 0.86,
      "notes": "game UI prompt text"
    }
  ]
}
```

- [ ] **步骤 2：写 parser 测试**

覆盖：
- 从 agent final message 中提取 JSON fenced block。
- locator 缺失时报错。
- translatedText 为空时报错。
- profile/resource 不匹配时报错。

- [ ] **步骤 3：实现 parser**

返回 `Datc64TranslationDraftProposal` 内部 record。不得直接写 overlay。

- [ ] **步骤 4：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~Datc64TranslationDraftParserTests
```

预期：PASS。

- [ ] **步骤 5：Commit**

```powershell
git add src\PoeStudio.Core\Agent\Datc64TranslationDraftParser.cs tests\PoeStudio.Tests\Datc64TranslationDraftParserTests.cs
git commit -m "feat(agent): parse DATC64 translation proposals"
```

### 任务 8：实现通用 AgentOrchestrator

**文件：**
- 创建：`src\PoeStudio.Storage\Agent\AgentOrchestrator.cs`
- 创建：`tests\PoeStudio.Tests\AgentOrchestratorTests.cs`

- [ ] **步骤 1：写 orchestrator 测试**

使用 fake `CodexProcessRunner` 或 runner interface 覆盖：
- `question` 创建 user message、run、initial plan、events，最终 run `Succeeded`，不创建 approval。
- `read-only-analysis` 创建 run 并保存 final result，不创建 approval。
- `datc64-translation` 解析 proposal 后 run 进入 `WaitingForApproval`，创建 pending approval，不写 overlay。
- runner 失败时 run `Failed`，保存 `RunFailed` event 和 stderr 摘要。
- retry 创建新的 run attempt，旧 run events 保留不被覆盖。

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentOrchestratorTests
```

预期：FAIL。

- [ ] **步骤 2：实现 AgentOrchestrator**

要求：
- API handler 不直接调用 `CodexProcessRunner`；必须调用 `AgentOrchestrator.StartRunAsync(...)`。
- orchestrator 从 `AgentCapabilities.GetRequired(taskKind)` 获取能力。
- orchestrator 调用 `AgentPromptBuilder` 生成 prompt。
- orchestrator 放在 `PoeStudio.Storage`，因为它需要 `AgentStore`。不得把它放进 `PoeStudio.Core` 后再给 `Core` 增加 `Storage` 引用。
- run 开始前写 `RunCreated` event 和 initial plan。
- final result 根据 taskKind 分流：只读任务保存 `ResultJson`，DATC64 创建 approval。
- 所有异常转成 `RunFailed` event，不抛出空 500。

- [ ] **步骤 3：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentOrchestratorTests
```

预期：PASS。

- [ ] **步骤 4：Commit**

```powershell
git add src\PoeStudio.Storage\Agent\AgentOrchestrator.cs tests\PoeStudio.Tests\AgentOrchestratorTests.cs
git commit -m "feat(agent): orchestrate runs across capabilities"
```

### 任务 9：审批后写入 DATC64 overlay draft

**文件：**
- 创建：`src\PoeStudio.Storage\Agent\Datc64DraftApplyService.cs`
- 创建：`tests\PoeStudio.Tests\Datc64DraftApplyServiceTests.cs`

- [ ] **步骤 1：运行影响分析**

通过 GitNexus MCP 工具运行：

```text
mcp__gitnexus__impact({
  "repo": "POE-Studio",
  "target": "TableInspector",
  "direction": "upstream"
})

mcp__gitnexus__impact({
  "repo": "POE-Studio",
  "target": "OverlayStore",
  "direction": "upstream"
})
```

只允许调用现有 public API，不修改 `TableInspector` 或 `OverlayStore`。
`Datc64DraftApplyService` 必须放在 `PoeStudio.Storage`，不得为了调用 `OverlayStore` 给 `PoeStudio.Core` 增加 `PoeStudio.Storage` 项目引用。

- [ ] **步骤 2：写 apply service 测试**

覆盖：
- `AgentApprovalStatus.Pending` 时调用 apply 返回 `approval_not_approved`，overlay list 为空。
- `Approved` 后生成 overlay entry。
- locator row/column 不存在返回 `locator_not_found`，不写 overlay。
- translatedText 与 sourceText 相同返回 warning，但允许写入。

- [ ] **步骤 3：实现 apply service**

流程：
1. 读取 approval proposal JSON。
2. 验证 approval status 为 `Approved`。
3. 用现有资源读取路径读取 base bytes。
4. 调用 `TableInspector().Inspect(...)`。
5. 将 candidates 转成 `TableCellEditDto(rowNumber: rowIndex + 1, columnIndex, value: translatedText)`。
6. 调用 `TableInspector.ApplyCellEditsToBytes(...)`。
7. 调用 `OverlayStore.SaveBytesAsync(profileId, resourcePath, editedBytes, basePhysicalPath, hasBasePhysicalPath, cancellationToken)`。
8. 更新 approval 为 `Applied`，记录 `OverlayDraftWritten` event。

- [ ] **步骤 4：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~Datc64DraftApplyServiceTests
```

预期：PASS。

- [ ] **步骤 5：Commit**

```powershell
git add src\PoeStudio.Storage\Agent\Datc64DraftApplyService.cs tests\PoeStudio.Tests\Datc64DraftApplyServiceTests.cs
git commit -m "feat(agent): apply approved DATC64 drafts"
```

### 任务 10：AgentRoutes 后端 API

**文件：**
- 创建：`src\PoeStudio.Api\AgentRoutes.cs`
- 修改：`src\PoeStudio.Api\Program.cs`
- 创建：`tests\PoeStudio.Tests\AgentApiSmokeTests.cs`

- [ ] **步骤 1：运行 API route impact**

通过 GitNexus MCP 工具运行：

```text
mcp__gitnexus__impact({
  "repo": "POE-Studio",
  "target": "Program",
  "direction": "upstream"
})
```

记录风险。只允许在 `Program.cs` 添加 DI 和 `app.MapAgentRoutes()`。

- [ ] **步骤 2：写 API smoke tests**

覆盖：
- `GET /api/agent/settings` 初次返回默认值。
- `POST /api/agent/settings` 保存后再次 GET 仍存在。
- `POST /api/agent/threads` 创建 thread。
- `POST /api/agent/threads/{threadId}/messages` 追加 user message 后 `GET /api/agent/threads/{threadId}` 可读取。
- `POST /api/agent/runs` 支持 `question`、`read-only-analysis`、`datc64-translation` 三类 taskKind。
- `POST /api/agent/runs/{runId}/retry` 创建新 run，旧 run events 仍可读取。
- `POST /api/agent/runs` 对缺少索引或 profile 返回结构化失败 run，不返回空 500。
- `GET /api/agent/runs/{runId}/events` 返回 events。
- `POST /api/agent/approvals/{id}/approve` 对不存在 approval 返回 404。

- [ ] **步骤 3：实现 AgentRoutes**

要求：
- 所有响应使用 `ApiResponse<T>`。
- 所有 route handler 调用 `AgentStore`。
- 启动 run 后使用后台 task，但必须通过 `AgentOrchestrator` 立即持久化 `RunCreated` event。
- 失败时写 `RunFailed` event。

- [ ] **步骤 4：Program 注册**

在 DI 中增加：

```csharp
builder.Services.AddScoped(sp => new AgentStore(sp.GetRequiredService<WorkspaceRootProvider>().CurrentRoot));
builder.Services.AddSingleton<CodexJsonEventParser>();
builder.Services.AddSingleton<AgentPromptBuilder>();
builder.Services.AddScoped<CodexProcessRunner>();
builder.Services.AddScoped<AgentOrchestrator>();
builder.Services.AddScoped<Datc64DraftApplyService>();
```

在 route 区增加：

```csharp
app.MapAgentRoutes();
```

- [ ] **步骤 5：运行 API tests**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentApiSmokeTests
```

预期：PASS。

- [ ] **步骤 6：Commit**

```powershell
git add src\PoeStudio.Api\AgentRoutes.cs src\PoeStudio.Api\Program.cs tests\PoeStudio.Tests\AgentApiSmokeTests.cs
git commit -m "feat(agent): expose Codex bridge API"
```

### 任务 11：Codex 多能力后端验收

**文件：**
- 修改：`tests\PoeStudio.Tests\AgentApiSmokeTests.cs`
- 创建：`docs\superpowers\reports\2026-05-22-poe-codex-bridge-stage2-acceptance.md`

- [ ] **步骤 1：写 fake Codex question 端到端测试**

fake Codex 输出：
- `mcp_tool_call` `poe_get_workspace`
- final agent message，包含只读 answer JSON fenced block。

测试流程：
1. 保存 agent settings 指向 fake codex。
2. 创建 thread。
3. 追加 user message。
4. 创建 `taskKind = question` run。
5. 等待 run 到 `Succeeded`。
6. 确认 events 里有 MCP tool call 和 final result。
7. 确认 approvals 为空，overlay list 为空。

- [ ] **步骤 2：写 fake Codex DATC64 端到端测试**

fake Codex 输出：
- `mcp_tool_call` `poe_datc64_extract_translatable_cells`
- final agent message，包含 DATC64 proposal JSON fenced block。

测试流程：
1. 创建 profile 和 physical DATC64 fixture。
2. 保存 agent settings 指向 fake codex。
3. 创建 thread。
4. 创建 run。
5. 等待 run 到 `WaitingForApproval`。
6. 确认 approval proposal 存在。
7. 确认 overlay list 为空。
8. approve approval。
9. 确认 overlay list 有对应 DATC64 draft。

- [ ] **步骤 3：运行端到端测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentApiSmokeTests"
```

预期：PASS。

- [ ] **步骤 4：真实 Codex smoke**

在开发机器上运行：

```powershell
codex mcp get poe-studio
dotnet test PoeStudio.sln --no-restore
```

然后启动 API，调用 `/api/agent/runs` 发起 DATC64 translation dry proposal。验收报告必须记录：
- run id
- Codex version
- mcp tool call event summary
- question run final result summary
- approval id
- approve 前 overlay 未写入
- approve 后 overlay entry

- [ ] **步骤 5：写验收报告**

验收报告必须包含：

```text
Stage 2 status: PASS
```

如果失败，写：

```text
Stage 2 status: FAIL
Failed command: 实际失败命令
Failure reason: 失败原因摘要
Next fix task id: 需要回到的任务编号
```

- [ ] **步骤 6：Commit**

```powershell
git add tests\PoeStudio.Tests\AgentApiSmokeTests.cs docs\superpowers\reports\2026-05-22-poe-codex-bridge-stage2-acceptance.md
git commit -m "docs(agent): record Stage 2 acceptance evidence"
```

---

## 5. Stage 2 完成判定

- [ ] Stage 1 已合入或当前基线包含 Stage 1，并有 `Stage 1 status: PASS`。
- [ ] `GET/POST /api/agent/settings` 能持久化模型和 Codex 设置。
- [ ] Agent thread/message/run/plan/event/approval 全部持久化，服务重启后可读取。
- [ ] `question` run 能通过 Codex + MCP 工具完成只读回答，并保存 result/events。
- [ ] `read-only-analysis` run 能通过 Codex + MCP 工具完成只读分析，并保存 result/events。
- [ ] `codex exec --json` 事件被记录为 run events。
- [ ] DATC64 run 能生成 translation proposal。
- [ ] approve 前不写 overlay。
- [ ] approve 后写 overlay draft。
- [ ] 失败 run 有 errorCode、errorMessage、stderr/event 证据。
- [ ] retry 会创建新 run attempt，不覆盖旧 run 证据。
- [ ] 没有正式 Agent Workspace UI。
- [ ] 没有任意 shell/文件写入工具。
- [ ] 没有 DATC64 专用主入口；DATC64 通过统一 `/api/agent/runs` 能力调度执行。
- [ ] `dotnet test PoeStudio.sln --no-restore` 通过。
- [ ] 验收报告写明 `Stage 2 status: PASS`。

---

## 6. 自检记录

- [ ] 覆盖最初目标：朝全能项目助手推进，而不是 DATC64 专用按钮。
- [ ] 覆盖 Stage 2 目标：通用后端 Agent runtime、Codex bridge、prompt builder、orchestrator、持久化、审批。
- [ ] 覆盖三类能力：`question`、`read-only-analysis`、`datc64-translation`。
- [ ] 不跑偏到 Stage 3 UI。
- [ ] 不把 `codex exec` 做成无状态工具入口。
- [ ] 写入 overlay 必须审批。
- [ ] 配置刷新不丢。
- [ ] 失败可追踪。
- [ ] 保留 Stage 3 接入口：API messages、events、approval、plan、thread 已可供 UI 消费。
