# POE Studio Agent Codex-first Planner Stage 4 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 把 Agent Workspace 从「前端/后端先选 taskKind，再让 Codex 执行」改为「Codex 先理解自然语言并生成结构化任务计划，POE Studio 校验安全边界后执行」。

**架构：** Stage 4 不新增后端关键词意图解析器，不让 POE Studio 用固定规则代替 Codex 判断任务。新增 Codex-first Planner：用户输入自然语言后，后端先启动一次受限 Planner Codex run，Planner 根据项目认知、当前 UI 上下文、会话历史、最近 run、可用工具和资源信息输出结构化计划；POE Studio 只负责 deterministic guardrails（能力白名单、profile/resource/Oodle/overlay/审批/写入边界校验），校验通过后复用 Stage 2 Agent runtime 执行计划。缺信息时返回澄清问题，不启动错误执行 run。

**技术栈：** .NET 8、ASP.NET Core Minimal API、xUnit、System.Text.Json、Codex CLI `exec --json`、POE Studio MCP Tools、现有 AgentStore/AgentOrchestrator/AgentPromptBuilder/Agent Workspace。

---

## 0. 固定硬约束

- [ ] **H0.1：Codex 是判断与规划大脑**  
  `auto` 模式必须先调用 Codex Planner 生成结构化计划。禁止新增或依赖 `AgentIntentResolver` 这类后端关键词规则判断器作为主路径。

- [ ] **H0.2：POE Studio 是安全员和执行底座**  
  POE Studio 可以校验、阻断、记录、审批、执行已批准的计划，但不能用本地规则静默替代 Codex 的任务理解。

- [ ] **H0.3：自然语言优先**  
  默认入口是自然语言。用户不应必须知道 `question`、`read-only-analysis`、`datc64-translation`、MCP 工具名或 overlay 细节。

- [ ] **H0.4：缺上下文先澄清**  
  Planner 判断缺少必要信息时，返回 `needs_clarification`，UI 展示问题并允许用户在同一会话继续回答。不得把缺资源的翻译任务降级成普通问答。

- [ ] **H0.5：写入审批门禁不变**  
  Codex 可以提出写入计划和 DATC64 proposal，但任何 overlay draft 写入仍必须等待 POE Studio approval 通过。Stage 4 不新增无审批写入。

- [ ] **H0.6：可追溯**  
  每次 auto run 必须保存：用户原始目标、Planner prompt 摘要、Planner 原始 JSON、Guard 校验结果、最终 resolved taskKind、继承的 profile/resource、warnings、clarification/blocker。刷新后必须可见。

- [ ] **H0.7：不扩大 token 成本**  
  Planner 使用项目认知摘要、会话摘要和轻量上下文，不把项目知识全文灌入 prompt。继续遵守项目认知层 prompt budget。

- [ ] **H0.8：可恢复与可重试**  
  Planner 成功但执行失败时，重试必须复用同一 Planner 结果，除非用户修改目标或点击「重新规划」。

- [ ] **H0.9：TDD 与 GitNexus**  
  每个行为先写失败测试。修改任何现有函数、类、方法前必须运行 GitNexus impact；提交前必须运行 GitNexus detect_changes。

---

## 1. 当前问题与正确目标

### 1.1 已完成基础

- Stage 1 已提供 POE Studio MCP 工具，让 Codex 能读取 workspace、profiles、index、resource、DATC64 cells 和项目上下文。
- Stage 2 已提供 Codex Bridge、thread/run/event/plan/approval/settings 持久化、DATC64 proposal 和 approval 后写 overlay。
- 项目认知层已把项目工作流知识按预算接入 prompt，并提供 `poe_get_project_context`。
- Stage 3 已提供 Agent Workspace UI、会话、事件、审批、刷新恢复。

### 1.2 当前偏差

当前 `/api/agent/runs` 要求调用方传入确定的 `taskKind`。前端默认 `自动/提问` 实际提交 `question`，导致：

- 「重新翻译刚才的表」会被当普通问答，而不是规划并执行 DATC64 翻译。
- 用户必须手动知道并选择内部任务类型。
- 后续如果用后端规则补 `taskKind` 判断，会继续滑向「高级脚本入口」。

### 1.3 Stage 4 目标行为

| 用户输入 | 上下文 | Codex Planner 输出 | Guard 行为 | 执行结果 |
| --- | --- | --- | --- | --- |
| `重新翻译刚才的表` | 最近 run 有 DATC64 resource | `datc64-translation`，继承 resourcePath | 校验 profile/resource/Oodle/overlay | 生成 proposal，等待审批 |
| `继续处理刚才资源，只处理和简体来源不同的内容` | 最近 run + 项目知识 | `datc64-translation`，包含差异处理约束 | 校验不直接写入，保留审批 | 生成符合约束的 proposal |
| `这个资源里有哪些文本` | UI 选中资源 | `read-only-analysis` | 校验 resource 存在 | 只读分析结果 |
| `POE Studio 当前工作态和 MCP 读取层区别是什么` | 无资源 | `question` | 无写入风险 | 普通回答 |
| `翻译这个表` | 无资源、无最近资源 | `needs_clarification` | 不启动执行 run | UI 要求补资源/profile |
| `帮我写个新工具直接批量改所有文件` | 缺少已批准能力 | `blocked` + missing capability proposal | 阻断，要求人工批准后续计划 | 不执行写入 |

---

## 2. 文件结构

### 创建

- `src/PoeStudio.Contracts/AgentPlannerDtos.cs`  
  Planner 输入、输出、Guard 结果、澄清响应 DTO。

- `src/PoeStudio.Core/Agent/AgentPlannerPromptBuilder.cs`  
  构建 Codex Planner prompt。职责是让 Codex 规划任务，不执行业务写入。

- `src/PoeStudio.Core/Agent/AgentTaskPlanParser.cs`  
  从 Codex final message 解析 fenced JSON，得到 `AgentTaskPlanDto`。

- `src/PoeStudio.Core/Agent/AgentTaskKindPolicy.cs`  
  区分「请求入口 taskKind」和「可执行能力 taskKind」。`auto` 是请求入口，不是执行能力；`question`、`read-only-analysis`、`datc64-translation` 是可执行能力。

- `src/PoeStudio.Storage/Agent/AgentPlanGuardService.cs`  
  deterministic guardrails。校验 taskKind 白名单、profile、resource、Oodle、overlay warning、写入审批边界。

- `tests/PoeStudio.Tests/AgentPlannerPromptBuilderTests.cs`
- `tests/PoeStudio.Tests/AgentTaskPlanParserTests.cs`
- `tests/PoeStudio.Tests/AgentPlanGuardServiceTests.cs`

### 修改

- `src/PoeStudio.Contracts/AgentDtos.cs`  
  追加 run 可追溯字段与必要状态。

- `src/PoeStudio.Core/Agent/AgentPromptBuilder.cs`  
  允许执行 prompt 接收 Planner 计划和用户约束，不再只依赖 taskKind。

- `src/PoeStudio.Storage/Agent/AgentStore.cs`  
  保持 JSON 兼容。必要时只新增读取最近 run 的方法；不得改变现有目录结构。

- `src/PoeStudio.Storage/Agent/AgentOrchestrator.cs`  
  新增 auto 两阶段流程：Plan -> Guard -> Execute。

- `src/PoeStudio.Api/AgentRoutes.cs`  
  `/api/agent/runs` 接收 `taskKind: "auto"`，调用 Codex-first Planner。

- `src/PoeStudio.Api/wwwroot/index.html`  
  默认任务类型改为 `auto`，文案改为「自动规划」。

- `src/PoeStudio.Api/wwwroot/app.js`  
  默认提交 `auto`，传入当前 UI profile/resource 上下文，展示 Planner/Guard/澄清结果。

- `tests/PoeStudio.Tests/AgentOrchestratorTests.cs`
- `tests/PoeStudio.Tests/AgentApiSmokeTests.cs`
- `tests/PoeStudio.Tests/FrontendAgentWorkspaceTests.cs`

---

## 3. 数据契约

### 3.1 `AgentDtos.cs` 追加字段

在 `AgentRunStatus` 末尾追加，不改变已有枚举数值：

```csharp
WaitingForInput = 7
```

在 `AgentRunDto` 末尾追加默认字段，保持旧 JSON 兼容：

```csharp
string? RequestedTaskKind = null,
string? ResolvedTaskKind = null,
string? PlannerJson = null,
string? GuardJson = null
```

### 3.2 新增 `AgentPlannerDtos.cs`

```csharp
namespace PoeStudio.Contracts;

public enum AgentTaskPlanStatus
{
    Ready = 0,
    NeedsClarification = 1,
    Blocked = 2
}

public sealed record AgentTaskPlanDto(
    AgentTaskPlanStatus Status,
    string RequestedTaskKind,
    string? ResolvedTaskKind,
    string ProfileId,
    string? ResourcePath,
    string Summary,
    IReadOnlyList<string> UserConstraints,
    IReadOnlyList<AgentTaskPlanStepDto> Steps,
    IReadOnlyList<string> RequiredApprovals,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Questions,
    AgentMissingCapabilityProposalDto? MissingCapability);

public sealed record AgentTaskPlanStepDto(
    int Order,
    string Title,
    string Reason,
    IReadOnlyList<string> SuggestedTools);

public sealed record AgentMissingCapabilityProposalDto(
    string CapabilityName,
    string Reason,
    IReadOnlyList<string> RequiredTools,
    IReadOnlyList<string> Risks);

public sealed record AgentPlanGuardResultDto(
    bool Ok,
    string? ErrorCode,
    string? ErrorMessage,
    string? ResolvedTaskKind,
    string ProfileId,
    string? ResourcePath,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Blockers);
```

### 3.3 请求 taskKind 与执行 taskKind

Stage 4 必须明确分开两类 taskKind：

| 类型 | 值 | 用途 |
| --- | --- | --- |
| 请求入口 taskKind | `auto` | 用户自然语言主入口，必须先走 Codex Planner，不允许传给 `AgentCapabilities.GetRequired`。 |
| 执行能力 taskKind | `question`、`read-only-analysis`、`datc64-translation` | Planner 输出并通过 Guard 后，才能传给 `AgentCapabilities.GetRequired` 和执行 prompt。 |

新增 `AgentTaskKindPolicy`：

```csharp
namespace PoeStudio.Core.Agent;

public static class AgentTaskKindPolicy
{
    public const string Auto = "auto";

    public static bool IsAuto(string taskKind)
    {
        return string.Equals(taskKind, Auto, StringComparison.Ordinal);
    }

    public static bool IsSupportedRequestTaskKind(string taskKind)
    {
        return IsAuto(taskKind) || AgentCapabilities.All.Any(x => string.Equals(x.TaskKind, taskKind, StringComparison.Ordinal));
    }

    public static bool IsExecutableTaskKind(string taskKind)
    {
        return AgentCapabilities.All.Any(x => string.Equals(x.TaskKind, taskKind, StringComparison.Ordinal));
    }
}
```

`auto` 不加入 `AgentCapabilities.All`，避免把 Planner 误当成可执行能力。

---

## 4. 任务 1：Planner DTO 和解析器

**文件：**
- 创建：`src/PoeStudio.Contracts/AgentPlannerDtos.cs`
- 创建：`src/PoeStudio.Core/Agent/AgentTaskPlanParser.cs`
- 创建：`src/PoeStudio.Core/Agent/AgentTaskKindPolicy.cs`
- 创建：`tests/PoeStudio.Tests/AgentTaskPlanParserTests.cs`
- 修改：`src/PoeStudio.Contracts/AgentDtos.cs`

- [x] **步骤 1：运行影响分析**

```text
mcp__gitnexus__impact({
  "repo": "POE-Studio",
  "target": "AgentRunDto",
  "direction": "upstream",
  "includeTests": true
})
```

记录风险。若 HIGH/CRITICAL，先停下报告。

- [x] **步骤 2：编写失败测试：解析 ready 计划**

在 `tests/PoeStudio.Tests/AgentTaskPlanParserTests.cs` 创建测试：

```csharp
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;

namespace PoeStudio.Tests;

public sealed class AgentTaskPlanParserTests
{
    [Fact]
    public void Parse_extracts_ready_plan_from_fenced_json()
    {
        var parser = new AgentTaskPlanParser();
        var message = """
        ```json
        {
          "status": "ready",
          "requestedTaskKind": "auto",
          "resolvedTaskKind": "datc64-translation",
          "profileId": "profile-target",
          "resourcePath": "data/balance/traditional chinese/activeskills.datc64",
          "summary": "Translate the selected DATC64 table.",
          "userConstraints": ["only translate cells different from simplified source"],
          "steps": [
            {
              "order": 1,
              "title": "Extract target DATC64 cells",
              "reason": "Need current editable state.",
              "suggestedTools": ["poe_datc64_extract_translatable_cells"]
            }
          ],
          "requiredApprovals": ["overlay_draft"],
          "warnings": [],
          "questions": [],
          "missingCapability": null
        }
        ```
        """;

        var plan = parser.Parse(message);

        Assert.Equal(AgentTaskPlanStatus.Ready, plan.Status);
        Assert.Equal("datc64-translation", plan.ResolvedTaskKind);
        Assert.Equal("profile-target", plan.ProfileId);
        Assert.Equal("data/balance/traditional chinese/activeskills.datc64", plan.ResourcePath);
        Assert.Contains(plan.UserConstraints, x => x.Contains("simplified", StringComparison.OrdinalIgnoreCase));
    }
}
```

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentTaskPlanParserTests
```

预期：FAIL，类型不存在。

- [x] **步骤 3：编写失败测试：解析 clarification**

追加测试：

```csharp
[Fact]
public void Parse_extracts_clarification_questions()
{
    var parser = new AgentTaskPlanParser();
    var message = """
    ```json
    {
      "status": "needs_clarification",
      "requestedTaskKind": "auto",
      "resolvedTaskKind": null,
      "profileId": "profile-1",
      "resourcePath": null,
      "summary": "The user asked to translate a table but no resource is known.",
      "userConstraints": [],
      "steps": [],
      "requiredApprovals": [],
      "warnings": [],
      "questions": ["请告诉我要翻译哪个资源路径，或先在资源列表中选中它。"],
      "missingCapability": null
    }
    ```
    """;

    var plan = parser.Parse(message);

    Assert.Equal(AgentTaskPlanStatus.NeedsClarification, plan.Status);
    Assert.Null(plan.ResolvedTaskKind);
    Assert.Single(plan.Questions);
}
```

- [x] **步骤 4：实现 DTO 和解析器**

`AgentTaskPlanParser.Parse` 必须：

- 优先提取 fenced `json` 代码块。
- 没有 fenced block 时尝试解析整段文本。
- 支持字符串枚举 `ready`、`needs_clarification`、`blocked`。
- 解析失败抛 `JsonException("planner_output_invalid")`。
- 文件顶部必须包含 `using System.Text.Json.Serialization;`，否则 `JsonStringEnumConverter` 无法编译。

核心实现形态：

```csharp
public sealed class AgentTaskPlanParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public AgentTaskPlanDto Parse(string finalMessage)
    {
        var json = ExtractJson(finalMessage);
        return JsonSerializer.Deserialize<AgentTaskPlanDto>(json, JsonOptions)
            ?? throw new JsonException("planner_output_invalid");
    }
}
```

- [x] **步骤 4.5：实现 `AgentTaskKindPolicy`**

创建 `src/PoeStudio.Core/Agent/AgentTaskKindPolicy.cs`，代码使用本文 3.3 中的完整实现。执行者必须确认 `auto` 没有加入 `AgentCapabilities.All`。

追加测试：

```csharp
[Fact]
public void Task_kind_policy_allows_auto_request_but_not_auto_execution()
{
    Assert.True(AgentTaskKindPolicy.IsSupportedRequestTaskKind("auto"));
    Assert.False(AgentTaskKindPolicy.IsExecutableTaskKind("auto"));
    Assert.True(AgentTaskKindPolicy.IsExecutableTaskKind("question"));
}
```

- [x] **步骤 5：运行测试验证通过**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentTaskPlanParserTests
```

预期：PASS。

- [x] **步骤 6：Commit**

```powershell
git add src\PoeStudio.Contracts\AgentDtos.cs src\PoeStudio.Contracts\AgentPlannerDtos.cs src\PoeStudio.Core\Agent\AgentTaskKindPolicy.cs src\PoeStudio.Core\Agent\AgentTaskPlanParser.cs tests\PoeStudio.Tests\AgentTaskPlanParserTests.cs
git commit -m "feat(agent): add codex planner plan contract"
```

---

## 5. 任务 2：Codex Planner Prompt Builder

**文件：**
- 创建：`src/PoeStudio.Core/Agent/AgentPlannerPromptBuilder.cs`
- 创建：`tests/PoeStudio.Tests/AgentPlannerPromptBuilderTests.cs`

- [ ] **步骤 1：运行影响分析**

本任务新增类，不修改现有符号。记录：`Impact: new planner prompt builder only`。

- [ ] **步骤 2：编写失败测试：prompt 明确让 Codex 判断**

```csharp
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;

namespace PoeStudio.Tests;

public sealed class AgentPlannerPromptBuilderTests
{
    [Fact]
    public void Build_instructs_codex_to_plan_not_execute()
    {
        var builder = new AgentPlannerPromptBuilder();
        var settings = new AgentSettingsDto("codex", null, null, "workspace-write", "poe-studio", "C:/repo", "manual");
        var thread = new AgentThreadDto("thread-1", "profile-target", "Agent", "Goal", "auto", AgentThreadStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var prompt = builder.Build(
            settings,
            thread,
            messages: [],
            goal: "重新翻译刚才的表",
            selectedResourcePath: null,
            recentRuns: [
                new AgentRunDto("run-1", "thread-1", "profile-target", "Translate", "datc64-translation", AgentRunStatus.WaitingForApproval, 90, "Waiting", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, null, null, null, "data/balance/traditional chinese/activeskills.datc64")
            ],
            capabilities: AgentCapabilities.All,
            projectContext: null);

        Assert.Contains("You are the planning brain", prompt);
        Assert.Contains("Do not execute the task", prompt);
        Assert.Contains("Return only a fenced JSON block", prompt);
        Assert.Contains("datc64-translation", prompt);
        Assert.Contains("data/balance/traditional chinese/activeskills.datc64", prompt);
        Assert.DoesNotContain("keyword classifier", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AgentCapabilities.GetRequired(\"auto\")", prompt, StringComparison.Ordinal);
    }
}
```

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentPlannerPromptBuilderTests
```

预期：FAIL，类型不存在。

- [ ] **步骤 3：实现 Planner prompt**

`AgentPlannerPromptBuilder.Build(...)` 必须包含：

- 用户原始目标。
- 当前 thread/profile。
- 最近 messages 摘要。
- 最近 runs 的 taskKind、resourcePath、status。
- 当前 UI 选中 resourcePath。
- 可用 capabilities 和 MCP 工具清单。
- 项目认知摘要。
- 输出 schema。
- 明确禁止执行写入和禁止直接声称完成。

关键文案必须包含：

```text
You are the planning brain for POE Studio Agent.
Do not execute the task in this planner pass.
Your job is to understand the user's natural-language request, decide what kind of work is needed, and produce a structured plan.
POE Studio will validate your plan before execution.
```

- [ ] **步骤 4：运行测试验证通过**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentPlannerPromptBuilderTests
```

预期：PASS。

- [ ] **步骤 5：Commit**

```powershell
git add src\PoeStudio.Core\Agent\AgentPlannerPromptBuilder.cs tests\PoeStudio.Tests\AgentPlannerPromptBuilderTests.cs
git commit -m "feat(agent): build codex-first planner prompt"
```

---

## 6. 任务 3：Plan Guard Service

**文件：**
- 创建：`src/PoeStudio.Storage/Agent/AgentPlanGuardService.cs`
- 创建：`tests/PoeStudio.Tests/AgentPlanGuardServiceTests.cs`

- [ ] **步骤 1：运行影响分析**

```text
mcp__gitnexus__impact({
  "repo": "POE-Studio",
  "target": "AgentStore",
  "direction": "upstream",
  "includeTests": true
})
```

本任务新增 service，但会读取 `ProfileStore`、`ResourceIndexStore`、`OverlayStore`。记录风险。

- [ ] **步骤 2：编写失败测试：DATC64 ready 计划通过并提示 overlay warning**

测试创建临时 workspace、profile、resource index、overlay entry，然后调用 guard。

```csharp
[Fact]
public async Task ValidateAsync_accepts_datc64_plan_and_warns_existing_overlay()
{
    using var workspace = new TemporaryWorkspace();
    var profileId = "profile-target";
    var resourcePath = "data/balance/traditional chinese/activeskills.datc64";
    await SaveProfileAndIndexedResourceAsync(workspace.Root, profileId, resourcePath);
    await new OverlayStore(workspace.Root).SaveBytesAsync(profileId, resourcePath, [1, 2, 3], CancellationToken.None);

    var guard = CreateGuard(workspace.Root);
    var plan = ReadyPlan(profileId, "datc64-translation", resourcePath, requiredApprovals: ["overlay_draft"]);
    var oodlePath = Path.Combine(workspace.Root, "oo2core.dll");
    await File.WriteAllBytesAsync(oodlePath, [], CancellationToken.None);

    var result = await guard.ValidateAsync(plan, oodlePath, CancellationToken.None);

    Assert.True(result.Ok);
    Assert.Equal("datc64-translation", result.ResolvedTaskKind);
    Assert.Contains(result.Warnings, x => x.Contains("overlay", StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **步骤 3：编写失败测试：写入能力没有 approval 被阻断**

```csharp
[Fact]
public async Task ValidateAsync_blocks_write_capability_without_required_approval()
{
    using var workspace = new TemporaryWorkspace();
    var profileId = "profile-target";
    var resourcePath = "metadata/example.datc64";
    await SaveProfileAndIndexedResourceAsync(workspace.Root, profileId, resourcePath);

    var guard = CreateGuard(workspace.Root);
    var plan = ReadyPlan(profileId, "datc64-translation", resourcePath, requiredApprovals: []);

    var result = await guard.ValidateAsync(plan, oodlePath: null, CancellationToken.None);

    Assert.False(result.Ok);
    Assert.Equal("approval_required", result.ErrorCode);
}
```

- [ ] **步骤 4：实现 Guard**

Guard 规则：

- `Status == NeedsClarification`：不视为错误，返回 `Ok = false`、`ErrorCode = "needs_clarification"`、`Blockers = Questions`。
- `Status == Blocked`：返回 `Ok = false`、`ErrorCode = "planner_blocked"`。
- `ResolvedTaskKind` 必须存在于 `AgentCapabilities.All`。
- `ProfileId` 必须存在。
- `datc64-translation` 必须有 `.datc64` resourcePath，且 resource index 中存在该资源。
- `datc64-translation` 必须包含 `overlay_draft` approval。
- `datc64-translation` 如果 `oodlePath` 非空但文件不存在，必须阻断并返回 `invalid_oodle_path`；如果为空，只 warning，不阻断，让现有运行时错误仍可审计。
- `ReadOnly` capability 不允许 `RequiredApprovals`。
- 发现已有 overlay 时只 warning，不阻断。
- 不执行任何写入。

- [ ] **步骤 5：运行测试验证通过**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentPlanGuardServiceTests
```

预期：PASS。

- [ ] **步骤 6：Commit**

```powershell
git add src\PoeStudio.Storage\Agent\AgentPlanGuardService.cs tests\PoeStudio.Tests\AgentPlanGuardServiceTests.cs
git commit -m "feat(agent): validate codex planner output"
```

---

## 7. 任务 4：Orchestrator 两阶段 auto 流程

**文件：**
- 修改：`src/PoeStudio.Storage/Agent/AgentOrchestrator.cs`
- 修改：`src/PoeStudio.Core/Agent/AgentPromptBuilder.cs`
- 修改：`tests/PoeStudio.Tests/AgentOrchestratorTests.cs`

- [ ] **步骤 1：运行影响分析**

```text
mcp__gitnexus__impact({
  "repo": "POE-Studio",
  "target": "AgentOrchestrator",
  "direction": "upstream",
  "includeTests": true
})
```

若风险 HIGH/CRITICAL，先报告再继续。

- [ ] **步骤 2：编写失败测试：auto 先规划再执行 DATC64**

在 `AgentOrchestratorTests.cs` 新增 fake runner：第一次返回 planner JSON，第二次返回 DATC64 proposal JSON。测试必须调用 `StartAutoRunShellAsync(...)` 后再调用 `ContinueRunAsync(run.Id, ...)`，对齐当前 API「先返回 shell run，再后台继续」模型。断言：

- run 最终 `WaitingForApproval`。
- `RequestedTaskKind == "auto"`。
- `TaskKind == "auto"`，用于保留用户请求入口。
- `ResolvedTaskKind == "datc64-translation"`。
- events 中有 `Planner completed` 和 `Plan guard passed`。
- approval 存在。

关键断言：

```csharp
Assert.Equal("auto", run.RequestedTaskKind);
Assert.Equal("auto", run.TaskKind);
Assert.Equal("datc64-translation", run.ResolvedTaskKind);
Assert.Contains(events, x => x.Message.Contains("Planner completed", StringComparison.OrdinalIgnoreCase));
Assert.Contains(events, x => x.Message.Contains("Plan guard passed", StringComparison.OrdinalIgnoreCase));
```

- [ ] **步骤 3：编写失败测试：needs clarification 不启动执行 runner 第二次**

fake runner 第一次返回 `needs_clarification`。断言：

- run 状态为 `WaitingForInput`。
- event 中有 clarification question。
- runner 调用次数为 1。
- 没有 approval。

- [ ] **步骤 4：实现 `StartAutoRunShellAsync`**

`StartAutoRunShellAsync` 只创建 shell run，不在 HTTP 请求线程里调用 Codex。实现路径：

1. 创建 user message。
2. 创建 run，`TaskKind = "auto"`，`RequestedTaskKind = "auto"`。
3. 保存初始 plan：`Ask Codex Planner`、`Validate plan`、`Execute approved plan`。
4. 返回 run，由现有 `StartBackgroundRun(run.Id, ...)` 调用 `ContinueRunAsync` 后台继续。

- [ ] **步骤 4.5：修改 `ContinueRunAsync`，让 auto 走 Planner 分支**

`ContinueRunAsync` 开头读取 run 后：

```csharp
if (AgentTaskKindPolicy.IsAuto(run.TaskKind))
{
    return await ContinueAutoRunAsync(run, cancellationToken);
}
```

`ContinueAutoRunAsync` 执行路径：

1. 读取 thread、settings、messages、recentRuns、projectContext。
2. 构建 planner prompt。
3. 第一次调用 `_runner.RunAsync(settings, plannerPrompt, ...)`。
4. 解析 final message 为 `AgentTaskPlanDto`。
5. 保存 `Planner completed` event，payload 为 planner JSON。
6. 调用 `AgentPlanGuardService.ValidateAsync(...)`。
7. 保存 `Plan guard passed` 或 `Plan guard blocked` event，payload 为 guard JSON。
8. 如果 `needs_clarification`，更新 run 为 `WaitingForInput`，`ProgressPercent = 40`，`Message = "Waiting for input"`，不执行第二次 runner。
9. 如果 blocked，更新 run 为 `Failed`，保留 blocker。
10. 如果 OK，创建 `executionRun = run with { ResolvedTaskKind = plan.ResolvedTaskKind, ResourcePath = guard.ResourcePath }`，但不要把 `TaskKind` 从 `auto` 改掉。
11. 第二次 runner 使用 `plan.ResolvedTaskKind` 取得 capability 并构建执行 prompt。
12. 执行结果沿用现有 DATC64 approval / read-only result 逻辑，但内部判断使用 resolved taskKind。

禁止把 `auto` 传给 `AgentCapabilities.GetRequired`。

- [ ] **步骤 5：让执行 prompt 接收 Planner 计划**

`AgentPromptBuilder.Build(...)` 增加 optional 参数：

```csharp
AgentTaskPlanDto? taskPlan = null
```

prompt 中必须包含：

```text
Planner-approved task plan:
- summary: ...
- user constraints: ...
- steps: ...
Follow this plan unless tool evidence proves it unsafe; if unsafe, stop and explain.
```

- [ ] **步骤 6：运行定向测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentOrchestratorTests
```

预期：PASS。

- [ ] **步骤 7：Commit**

```powershell
git add src\PoeStudio.Storage\Agent\AgentOrchestrator.cs src\PoeStudio.Core\Agent\AgentPromptBuilder.cs tests\PoeStudio.Tests\AgentOrchestratorTests.cs
git commit -m "feat(agent): run auto tasks through codex planner"
```

---

## 8. 任务 5：API 接入 `taskKind: auto`

**文件：**
- 修改：`src/PoeStudio.Api/AgentRoutes.cs`
- 修改：`src/PoeStudio.Api/Program.cs`
- 修改：`tests/PoeStudio.Tests/AgentApiSmokeTests.cs`

- [ ] **步骤 1：运行影响分析**

```text
mcp__gitnexus__api_impact({
  "repo": "POE-Studio",
  "route": "/api/agent/runs"
})
```

记录消费者与响应字段风险。

- [ ] **步骤 2：编写失败测试：API auto 翻译进入审批**

在 `AgentApiSmokeTests.cs` 新增：

```csharp
[Fact]
public async Task Agent_run_auto_uses_codex_planner_and_reaches_datc64_approval()
{
    var client = _factory.CreateClient();
    var resourcePath = "data/balance/traditional chinese/activeskills.datc64";
    await SaveIndexedResourceAsync("profile-1", resourcePath);
    _runner.EnqueuePlannerReady("datc64-translation", "profile-1", resourcePath);
    _runner.EnqueueDatc64Proposal(resourcePath);

    var thread = await CreateThreadAsync(client, "auto");
    var response = await client.PostAsJsonAsync("/api/agent/runs", new AgentRunCreateRequest(
        thread.Id,
        thread.ProfileId,
        "重新翻译刚才的表",
        "auto",
        null));

    response.EnsureSuccessStatusCode();
    var run = (await response.Content.ReadFromJsonAsync<ApiResponse<AgentRunDto>>())!.Data!;
    run = await WaitForRunStatusAsync(client, run.Id, AgentRunStatus.WaitingForApproval);

    Assert.Equal("auto", run.RequestedTaskKind);
    Assert.Equal("auto", run.TaskKind);
    Assert.Equal("datc64-translation", run.ResolvedTaskKind);
}
```

- [ ] **步骤 3：编写失败测试：API clarification 返回可恢复状态**

fake planner 返回 `needs_clarification`。断言 run 为 `WaitingForInput`，snapshot 中可看到 question event。

- [ ] **步骤 4：实现 API 分流**

`POST /api/agent/runs`：

- `taskKind == "auto"`：调用 `StartAutoRunShellAsync`。
- 显式 taskKind：保留高级模式旧路径。
- 不再在 API 层对 `auto` 做 DATC64 resourcePath 强制校验。
- DI 注册 `AgentPlannerPromptBuilder`、`AgentTaskPlanParser`、`AgentPlanGuardService`。

`POST /api/agent/threads`：

- 把 `AgentCapabilities.GetRequired(request.TaskKind)` 改为 `AgentTaskKindPolicy.IsSupportedRequestTaskKind(request.TaskKind)`。
- `auto` 可以创建 thread，但仍不能作为执行 capability。
- unsupported 仍返回 `unsupported_task_kind`。

- [ ] **步骤 5：运行 API 测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentApiSmokeTests
```

预期：PASS。

- [ ] **步骤 6：Commit**

```powershell
git add src\PoeStudio.Api\AgentRoutes.cs src\PoeStudio.Api\Program.cs tests\PoeStudio.Tests\AgentApiSmokeTests.cs
git commit -m "feat(agent): expose auto planner runs"
```

---

## 9. 任务 6：Agent Workspace 改为 auto-first

**文件：**
- 修改：`src/PoeStudio.Api/wwwroot/index.html`
- 修改：`src/PoeStudio.Api/wwwroot/app.js`
- 修改：`src/PoeStudio.Api/wwwroot/styles.css`
- 修改：`tests/PoeStudio.Tests/FrontendAgentWorkspaceTests.cs`

- [ ] **步骤 1：编写失败测试：默认任务类型是 auto**

在 `FrontendAgentWorkspaceTests.cs` 更新或新增断言：

```csharp
Assert.Contains("<option value=\"auto\" selected>自动规划</option>", html);
Assert.DoesNotContain("<option value=\"question\">自动/提问</option>", html);
Assert.Contains("taskKind = $(\"agentTaskKindSelect\").value || \"auto\"", js);
```

- [ ] **步骤 2：编写失败测试：auto 会携带当前资源上下文**

断言 `app.js` 不再只有 DATC64 才传 resourcePath：

```csharp
Assert.Contains("selectedResourcePath", js);
Assert.DoesNotContain("resourcePath: taskKind === \"datc64-translation\" ? resourcePath : null", js);
```

- [ ] **步骤 3：修改 HTML**

任务类型选择：

```html
<option value="auto" selected>自动规划</option>
<option value="question">强制提问</option>
<option value="read-only-analysis">强制只读分析</option>
<option value="datc64-translation">强制 DATC64 翻译</option>
```

高级选择可以保留，但默认 UI 文案必须让用户知道「自动规划」是主入口。

- [ ] **步骤 4：修改 JS**

`startAgentRun` 逻辑：

- 默认 `taskKind = "auto"`。
- 获取当前 UI 选中资源路径作为 `selectedResourcePath`。
- `resourcePath` 对 auto 也传递当前选中资源；没有则传 `null`。
- 展示 run 的 `requestedTaskKind`、`resolvedTaskKind`、Planner/Guard events。
- `WaitingForInput` 状态展示 clarification question，不显示失败。

- [ ] **步骤 5：运行前端 smoke 测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~FrontendAgentWorkspaceTests
```

预期：PASS。

- [ ] **步骤 6：Commit**

```powershell
git add src\PoeStudio.Api\wwwroot\index.html src\PoeStudio.Api\wwwroot\app.js src\PoeStudio.Api\wwwroot\styles.css tests\PoeStudio.Tests\FrontendAgentWorkspaceTests.cs
git commit -m "feat(agent): make workspace auto planner first"
```

---

## 10. 任务 7：验收与防回归

**文件：**
- 创建：`docs/superpowers/reports/2026-05-23-poe-agent-codex-first-planner-stage4-acceptance.md`
- 修改：相关测试文件（仅当补充断言）

- [ ] **步骤 1：运行定向测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentTaskPlanParserTests|FullyQualifiedName~AgentPlannerPromptBuilderTests|FullyQualifiedName~AgentPlanGuardServiceTests|FullyQualifiedName~AgentOrchestratorTests|FullyQualifiedName~AgentApiSmokeTests|FullyQualifiedName~FrontendAgentWorkspaceTests"
```

预期：全部 PASS。

- [ ] **步骤 2：运行 Agent/DATC64 相关测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~Agent|FullyQualifiedName~Mcp|FullyQualifiedName~Datc64"
```

预期：全部 PASS。

- [ ] **步骤 3：运行全量测试**

```powershell
dotnet test PoeStudio.sln --no-restore
```

预期：全部 PASS。

- [ ] **步骤 4：运行 GitNexus 变更检测**

```text
mcp__gitnexus__detect_changes({
  "repo": "POE-Studio",
  "scope": "all"
})
```

预期：只影响 Agent Planner、Agent API、Agent Workspace 相关流程。若 HIGH/CRITICAL，报告并补充风险说明。

- [ ] **步骤 5：实机验收 1：自然语言翻译**

条件：

- profile：国际服-目标。
- resource：`data/balance/traditional chinese/activeskills.datc64`。
- Oodle：已配置。

操作：

1. 打开 Agent Workspace。
2. 任务类型保持「自动规划」。
3. 输入：`重新翻译刚才的表，只处理目标表和简体来源不一致的单元格，英文或相同内容不改。`
4. 启动 run。

验收：

- UI 显示 Planner 阶段。
- Planner resolved taskKind 为 `datc64-translation`。
- Guard 显示 profile/resource/Oodle/overlay 预检结果。
- run 进入 `WaitingForApproval`。
- approval proposal 中包含候选，不自动写 overlay。

- [ ] **步骤 6：实机验收 2：缺资源澄清**

操作：

1. 新建 Agent 会话，不选择资源。
2. 输入：`翻译这个表。`
3. 启动 run。

验收：

- run 进入 `WaitingForInput`。
- UI 显示 Codex Planner 提出的问题。
- 没有 DATC64 proposal。
- 没有 overlay 写入。

- [ ] **步骤 7：实机验收 3：普通项目问题**

输入：

```text
POE Studio 当前工作态和 MCP 读取层有什么区别？
```

验收：

- Planner resolved taskKind 为 `question`。
- run 不产生 approval。
- 回答引用项目认知层。

- [ ] **步骤 8：写验收报告**

报告必须包含：

- 测试命令和结果。
- GitNexus detect_changes 结果。
- 三个实机验收的 threadId/runId。
- Planner 原始 JSON 摘要。
- Guard warnings/blockers 摘要。
- 明确说明 Stage 4 仍不是「全能自动写代码 Agent」，只是把判断和规划主路径改回 Codex-first。

- [ ] **步骤 9：Commit**

```powershell
git add docs\superpowers\reports\2026-05-23-poe-agent-codex-first-planner-stage4-acceptance.md
git commit -m "docs(agent): record codex-first planner stage4 acceptance"
```

---

## 11. 非目标

- [ ] 本阶段不实现 Codex 自动创建新工具或脚本。
- [ ] 本阶段不新增任意 shell 执行能力。
- [ ] 本阶段不允许 Codex 直接写项目代码或游戏资源。
- [ ] 本阶段不实现长期记忆向量库。
- [ ] 本阶段不替代后续「缺能力 -> 提案 -> 用户批准 -> 创建工具 -> 验收工具」完整能力闭环。

---

## 12. 后续阶段接口预留

Stage 4 完成后，后续阶段应基于 Planner 输出扩展：

- `missingCapability`：当 Codex 判断现有工具不足时，生成能力提案。
- `requiredTools`：声明需要新增或组合哪些工具。
- `risks`：声明新工具风险。
- `requiredApprovals`：声明创建工具、执行脚本、写项目文件等审批点。

下一阶段正确方向：

1. Codex Planner 发现缺能力。
2. POE Studio 展示能力提案和风险。
3. 用户批准后，进入受控工具创建计划。
4. 工具必须有测试、沙箱、权限范围、验收记录。
5. 工具通过后，Agent 才能使用它完成原任务。

---

## 13. 自检清单

- [ ] 计划没有让 POE Studio 后端关键词规则替代 Codex 判断。
- [ ] `auto` 是默认主路径，不再等同 `question`。
- [ ] Planner 与 Guard 分层明确：Codex 判断，POE Studio 校验。
- [ ] 缺上下文不会误跑普通问答。
- [ ] 写入仍走 approval。
- [ ] Planner/Guard/执行结果全部可追溯。
- [ ] prompt budget 没有全文灌入。
- [ ] 保留 Stage1/2/3 基础设施，不推倒重来。
- [ ] 每个任务都有测试、实现、验证、commit 步骤。
