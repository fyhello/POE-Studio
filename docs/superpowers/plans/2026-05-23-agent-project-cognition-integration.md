# POE Studio Agent 项目认知层接入实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 把已验收的 POE Studio 项目工作流知识底座接入 Stage 2 Agent runtime，让 Agent 每次 run 前自动理解项目语义、工作流、工具边界、读取层和审批规则，而不是依赖用户反复补充长提示。

**架构：** 新增只读“项目认知层”：后端通过 repository root resolver 找到仓库知识文档，`AgentProjectContextService` 读取并摘要化 `docs/agent/poe-studio-project-workflows.md`、`docs/agent/poe-studio-agent-context.md`、`docs/ai-project-memory.md`；`AgentProjectContextSelector` 按任务选择相关章节；`AgentPromptBuilder` 把摘要、读取层、工具限制、风险审批边界注入 prompt；`AgentOrchestrator` 每次 run 前记录 project-context preflight event 和 plan evidence；MCP 新增只读 `poe_get_project_context`，供 Codex 在执行中主动查询。DATC64 只作为验收样例，不是本计划主线。

**技术栈：** .NET 8、ASP.NET Core Minimal API、System.Text.Json、POE Studio Core/Storage/Mcp/Contracts、xUnit、Codex CLI JSONL、Markdown 项目知识文档。

---

## 0. 固定硬约束

- [x] **H0.1：目标是 Agent 项目认知，不是 DATC64 专项修复**  
  本计划不实现 overlay-aware DATC64 读取、不改 DATC64 proposal schema、不新增 DATC64 对比写入规则。DATC64 只用于验证 Agent 是否会理解“目标当前工作态、来源参考表、MCP 读取层限制、审批边界”。

- [x] **H0.2：知识底座必须进入运行链路**  
  `docs/agent/poe-studio-project-workflows.md` 必须由运行时读取或摘要化。禁止只在 prompt 中手写几条规则冒充接入。

- [x] **H0.3：每个 run 必须留下可追溯证据**  
  run events 或 plan evidence 中必须能看到：项目上下文已加载、来源文档路径、hash 或版本、摘要、preflight 结果、warnings。

- [x] **H0.4：保持 Stage 2 边界**  
  允许改后端 Agent runtime、Core prompt、MCP 只读工具、测试和验收报告。禁止新增正式 Agent Workspace UI，禁止新增任意 shell 工具，禁止自动写代码工具，禁止绕过 approval 写 overlay。

- [x] **H0.5：区分 repository root 与 workspace root**  
  AgentStore 使用 POE Studio 用户 workspace；项目知识文档位于代码仓库。实现者必须新增 repository root 解析，不得直接把 `WorkspaceRootProvider.CurrentRoot` 当作仓库根。`AgentSettingsDto.WorkingDirectory` 必须作为 repository root 解析的优先候选之一，因为 API 进程可能不是从仓库根启动。

- [x] **H0.6：知识注入要可控**  
  禁止每次把整篇知识底座无脑塞进 prompt。必须有摘要、章节选择、长度上限和整条 prompt budget 验收，避免 token 失控。执行完成后必须用 fake runner 捕获 prompt，并证明 prompt 没有携带整篇知识底座原文。

- [x] **H0.7：计划与进度可追溯**  
  执行者必须逐项更新本计划复选框。偏离计划时先补充计划说明，再继续实现。

---

## 1. 当前代码事实与接入边界

### 已存在文件

- `src/PoeStudio.Contracts/AgentDtos.cs`  
  已定义 Stage 2 thread/message/run/event/plan/approval/settings/capability DTO。

- `src/PoeStudio.Core/Agent/AgentPromptBuilder.cs`  
  当前 `Build(...)` 只有 settings、capability、thread、messages、goal、resourcePath 参数，尚无 project context 参数。

- `src/PoeStudio.Core/Agent/AgentCapabilities.cs`  
  当前能力为 `question`、`read-only-analysis`、`datc64-translation`，MCP 工具清单尚不包含 `poe_get_project_context`。

- `src/PoeStudio.Storage/Agent/AgentOrchestrator.cs`  
  当前每次 run 创建 initial plan：`Build prompt`、`Run Codex`、`Store result`，尚未加载项目认知上下文。

- `src/PoeStudio.Api/AgentRoutes.cs`  
  Stage 2 API 路由已存在，本计划不新增正式 UI，不新建 Agent 主路由文件。

- `src/PoeStudio.Api/Program.cs`  
  已注册 AgentStore、AgentPromptBuilder、CodexProcessRunner、AgentOrchestrator、Datc64DraftApplyService。此计划只允许增加 repository root resolver 和 project context service 的 DI。

- `src/PoeStudio.Mcp/PoeMcpTools.cs`  
  已注册 Stage 1 只读工具，尚无 `poe_get_project_context`。

### 必须新增文件

- `src/PoeStudio.Contracts/AgentProjectContextDtos.cs`  
  项目认知 DTO。

- `src/PoeStudio.Core/Agent/AgentRepositoryRootResolver.cs`  
  从显式路径、环境变量、当前目录、AppContext.BaseDirectory 祖先目录中定位仓库根。

- `src/PoeStudio.Core/Agent/AgentProjectContextService.cs`  
  只读读取并摘要化项目知识文档。

- `src/PoeStudio.Core/Agent/AgentProjectContextSelector.cs`  
  按任务选择相关知识章节。

- `tests/PoeStudio.Tests/AgentRepositoryRootResolverTests.cs`
- `tests/PoeStudio.Tests/AgentProjectContextServiceTests.cs`
- `tests/PoeStudio.Tests/AgentProjectContextSelectorTests.cs`
- `tests/PoeStudio.Tests/AgentProjectContextBudgetTests.cs`
- `tests/PoeStudio.Tests/McpProjectContextToolTests.cs`
- `docs/superpowers/reports/2026-05-23-agent-project-cognition-integration-acceptance.md`

### 必须修改文件

- `src/PoeStudio.Core/Agent/AgentPromptBuilder.cs`
- `src/PoeStudio.Storage/Agent/AgentOrchestrator.cs`
- `src/PoeStudio.Core/Agent/AgentCapabilities.cs`
- `src/PoeStudio.Mcp/PoeMcpTools.cs`
- `src/PoeStudio.Api/Program.cs`
- `tests/PoeStudio.Tests/AgentPromptBuilderTests.cs`
- `tests/PoeStudio.Tests/AgentOrchestratorTests.cs`
- `tests/PoeStudio.Tests/AgentCapabilitiesTests.cs`
- `tests/PoeStudio.Tests/McpToolRegistryTests.cs`
- `tests/PoeStudio.Tests/McpPoeToolsTests.cs`
- `tests/PoeStudio.Tests/AgentApiSmokeTests.cs`

---

## 2. Token 与 prompt budget 硬门槛

- [x] **B0.1：Project context 输出预算**  
  `AgentProjectContextService` 输出的 `Summary` 最大 2500 字符；每个 `RelevantSections[*].Content`、`ToolGuidance[*].UseFor + Limitation`、`RiskBoundaries[*].Rule` 最大 900 字符。

- [x] **B0.2：Prompt 总长度预算**  
  `AgentPromptBuilder.Build(...)` 生成的完整 prompt 在测试中必须小于 16000 字符。若未来确有需要扩大，必须先更新本计划或后续计划，并说明原因、任务类型和验收标准。

- [x] **B0.3：禁止全文灌入**  
  fake runner 捕获的 prompt 不得包含 `docs/agent/poe-studio-project-workflows.md` 中任一长章节的连续原文。测试至少选择一个超过 1200 字符的章节，断言 prompt 不包含该章节全文。

- [x] **B0.4：任务相关性验收**  
  DATC64 翻译任务 prompt 应包含 `layering`、`datc64`、`mcp`、`approval` 相关摘要；不应携带大段无关 patch/import/native 章节全文。Patch/Native 任务可以包含 patch/native 摘要，但不应携带 DATC64 章节全文。

- [x] **B0.5：MCP 按需查询，不替代预算**  
  `poe_get_project_context` 允许 Codex 执行中按需查询项目上下文，但工具返回也必须遵守同类摘要和 section 长度限制。MCP 工具不能返回整篇知识底座全文。

---

## 3. 任务 1：Repository root 解析

**文件：**
- 创建：`src/PoeStudio.Core/Agent/AgentRepositoryRootResolver.cs`
- 创建：`tests/PoeStudio.Tests/AgentRepositoryRootResolverTests.cs`

- [x] **步骤 1：运行影响分析**

通过 GitNexus MCP：

```text
mcp__gitnexus__impact({
  "repo": "POE-Studio",
  "target": "AgentPromptBuilder",
  "direction": "upstream"
})
```

本任务只新增类，不修改现有符号；记录后续 prompt/orchestrator 影响面。

- [x] **步骤 2：编写失败测试**

测试必须覆盖：

- 从包含 `PoeStudio.sln` 和 `docs/agent/poe-studio-project-workflows.md` 的目录识别仓库根。
- 从子目录向上查找仓库根。
- 显式 repository root 优先。
- `ResolveFromCandidates(settings.WorkingDirectory, ...)` 能把 Agent 设置中的 working directory 作为优先候选。
- 环境变量 `POE_STUDIO_REPOSITORY_ROOT` 可作为 fallback。
- 找不到时返回 `null`，不抛异常。

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentRepositoryRootResolverTests
```

预期：FAIL，类型不存在。

- [x] **步骤 3：实现 resolver**

接口：

```csharp
public sealed class AgentRepositoryRootResolver
{
    public AgentRepositoryRootResolver(string? explicitRoot = null);

    public string? Resolve();

    public string? ResolveFromCandidates(params string?[] candidates);
}
```

有效仓库根条件：

- 目录存在。
- 同时存在 `PoeStudio.sln`。
- 存在 `docs/agent/poe-studio-project-workflows.md`。

候选顺序：

1. explicitRoot。
2. `POE_STUDIO_REPOSITORY_ROOT`。
3. `Environment.CurrentDirectory` 及祖先。
4. `AppContext.BaseDirectory` 及祖先。

- [x] **步骤 4：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentRepositoryRootResolverTests
```

预期：PASS。

- [x] **步骤 5：Commit**

```powershell
git add src\PoeStudio.Core\Agent\AgentRepositoryRootResolver.cs tests\PoeStudio.Tests\AgentRepositoryRootResolverTests.cs
git commit -m "feat(agent): resolve repository root for project context"
```

---

## 4. 任务 2：项目认知 DTO

**文件：**
- 创建：`src/PoeStudio.Contracts/AgentProjectContextDtos.cs`
- 修改：`tests/PoeStudio.Tests/AgentProjectContextServiceTests.cs`

- [x] **步骤 1：编写 DTO 序列化失败测试**

测试 `AgentProjectContextDto` 序列化后包含：

- `version`
- `sources`
- `summary`
- `relevantSections`
- `toolGuidance`
- `riskBoundaries`
- `unknowns`

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentProjectContextServiceTests
```

预期：FAIL，DTO 类型不存在。

- [x] **步骤 2：创建 DTO**

必须定义：

```csharp
public sealed record AgentProjectContextDto(
    string Version,
    IReadOnlyList<AgentProjectContextSourceDto> Sources,
    string Summary,
    IReadOnlyList<AgentProjectContextSectionDto> RelevantSections,
    IReadOnlyList<AgentToolGuidanceDto> ToolGuidance,
    IReadOnlyList<AgentRiskBoundaryDto> RiskBoundaries,
    IReadOnlyList<string> Unknowns);

public sealed record AgentProjectContextSourceDto(
    string Path,
    bool Exists,
    string? Hash,
    DateTimeOffset? LastModifiedAt);

public sealed record AgentProjectContextSectionDto(
    string Key,
    string Title,
    string Content);

public sealed record AgentToolGuidanceDto(
    string ToolName,
    string UseFor,
    string Limitation);

public sealed record AgentRiskBoundaryDto(
    string Action,
    string RiskLevel,
    bool RequiresApproval,
    string Rule);

public sealed record AgentProjectPreflightDto(
    string ThreadId,
    string RunId,
    string ProfileId,
    string TaskKind,
    string Goal,
    string? ResourcePath,
    bool ProjectContextLoaded,
    string? RepositoryRoot,
    IReadOnlyList<AgentProjectContextSourceDto> Sources,
    string Summary,
    IReadOnlyList<string> RequiredChecks,
    IReadOnlyList<string> Warnings);
```

- [x] **步骤 3：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentProjectContextServiceTests
```

预期：PASS 当前 DTO 测试。

- [x] **步骤 4：Commit**

```powershell
git add src\PoeStudio.Contracts\AgentProjectContextDtos.cs tests\PoeStudio.Tests\AgentProjectContextServiceTests.cs
git commit -m "feat(agent): define project context contracts"
```

---

## 5. 任务 3：读取和摘要化项目知识底座

**文件：**
- 创建：`src/PoeStudio.Core/Agent/AgentProjectContextService.cs`
- 修改：`tests/PoeStudio.Tests/AgentProjectContextServiceTests.cs`

- [x] **步骤 1：编写服务失败测试**

测试必须覆盖：

- 能读取 `docs/agent/poe-studio-project-workflows.md`。
- source 包含 exists/hash/lastModifiedAt。
- DATC64 翻译任务摘要包含 overlay/current working state/MCP 读取层限制。
- 缺失文档时不抛异常，`Unknowns` 记录 missing。
- 摘要长度不超过 2500 字符。
- section content 单条不超过 900 字符。
- service 不返回任何单篇知识文档全文。

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentProjectContextServiceTests
```

预期：FAIL。

- [x] **步骤 2：实现 service**

接口：

```csharp
public sealed class AgentProjectContextService
{
    public AgentProjectContextService(AgentRepositoryRootResolver repositoryRootResolver);

    public Task<AgentProjectContextDto> BuildAsync(
        string taskKind,
        string goal,
        string? resourcePath,
        string? repositoryRootCandidate,
        CancellationToken cancellationToken);
}
```

默认读取：

- `docs/agent/poe-studio-project-workflows.md`
- `docs/agent/poe-studio-agent-context.md`
- `docs/ai-project-memory.md`

实现要求：

- 使用 repository root resolver，不使用 workspace root 当仓库根。
- 使用 `repositoryRootCandidate` 作为第一候选；orchestrator 必须传入 `settings.WorkingDirectory`。
- Markdown 章节提取只用标题切片，不引入新依赖。
- source hash 使用 SHA-256。
- 缺失文档返回成功 context，但写入 `Unknowns`。
- 默认工具边界必须包含：`poe_get_workspace`、`poe_list_profiles`、`poe_get_index_status`、`poe_search_resources`、`poe_read_resource`、`poe_datc64_extract_translatable_cells`、`poe_get_project_context`。
- 默认风险边界必须包含：读取资源、生成 proposal、写 overlay、批量写入、构建补丁、安装/回滚。
- `AgentProjectContextServiceTests` 必须包含 budget 测试：构造一个 8000 字符以上的知识文档，断言 summary 和 sections 被截断，且 context 中不存在整篇原文。

- [x] **步骤 3：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentProjectContextServiceTests
```

预期：PASS。

- [x] **步骤 4：Commit**

```powershell
git add src\PoeStudio.Core\Agent\AgentProjectContextService.cs tests\PoeStudio.Tests\AgentProjectContextServiceTests.cs
git commit -m "feat(agent): load project workflow context"
```

---

## 6. 任务 4：任务相关章节选择

**文件：**
- 创建：`src/PoeStudio.Core/Agent/AgentProjectContextSelector.cs`
- 创建：`tests/PoeStudio.Tests/AgentProjectContextSelectorTests.cs`
- 修改：`src/PoeStudio.Core/Agent/AgentProjectContextService.cs`

- [x] **步骤 1：编写 selector 失败测试**

测试必须覆盖：

- `datc64-translation` + `.datc64` + “继续翻译当前表” 选择 `layering`、`datc64`、`mcp`、`approval`。
- “构建补丁 / 安装 / 回滚 / Oodle / Bundles2” 选择 `patch`、`native`、`approval`。
- “搜索资源 / 找资源 / 索引” 选择 `index`、`resource`。
- 任意任务默认包含 `overview`、`workflow`、`risk`。

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentProjectContextSelectorTests
```

预期：FAIL。

- [x] **步骤 2：实现 selector**

接口：

```csharp
public static class AgentProjectContextSelector
{
    public static IReadOnlyList<string> SelectKeys(string taskKind, string goal, string? resourcePath);
}
```

规则：

- `.datc64`、`表`、`翻译`、`草稿`、`当前` 命中 DATC64/读取层/审批。
- `构建`、`补丁`、`安装`、`回滚` 命中 patch/overlay/approval。
- `Native`、`GGPK`、`Oodle`、`Bundles2` 命中 native。
- `找`、`搜索`、`资源`、`索引` 命中 index/resource。

- [x] **步骤 3：service 接入 selector**

`AgentProjectContextService.BuildAsync` 使用 selector 选择章节；未找到章节时保留 warnings/unknowns，不失败。

- [x] **步骤 4：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentProjectContextSelectorTests|FullyQualifiedName~AgentProjectContextServiceTests"
```

预期：PASS。

- [x] **步骤 5：Commit**

```powershell
git add src\PoeStudio.Core\Agent\AgentProjectContextSelector.cs src\PoeStudio.Core\Agent\AgentProjectContextService.cs tests\PoeStudio.Tests\AgentProjectContextSelectorTests.cs tests\PoeStudio.Tests\AgentProjectContextServiceTests.cs
git commit -m "feat(agent): select relevant project context"
```

---

## 7. 任务 5：PromptBuilder 注入项目认知

**文件：**
- 修改：`src/PoeStudio.Core/Agent/AgentPromptBuilder.cs`
- 修改：`tests/PoeStudio.Tests/AgentPromptBuilderTests.cs`

- [x] **步骤 1：运行影响分析**

通过 GitNexus MCP：

```text
mcp__gitnexus__impact({
  "repo": "POE-Studio",
  "target": "AgentPromptBuilder",
  "direction": "upstream"
})
```

若 HIGH 或 CRITICAL，先记录直接调用方和测试影响面。

- [x] **步骤 2：编写 prompt 失败测试**

新增测试断言 prompt 包含：

- `Project context`
- `current working state`
- `overlay`
- `poe_get_project_context`
- `No useOverlay parameter` 或等价工具限制
- `Requires approval`
- `unknowns`
- 完整 prompt 长度小于 16000 字符。
- prompt 不包含任一超过 1200 字符的知识底座章节全文。

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentPromptBuilderTests
```

预期：FAIL。

- [x] **步骤 3：修改 Build 签名并保留兼容 overload**

新增主签名：

```csharp
public string Build(
    AgentSettingsDto settings,
    AgentCapabilityDto capability,
    AgentThreadDto thread,
    IReadOnlyList<AgentMessageDto> messages,
    string goal,
    string? resourcePath,
    AgentProjectContextDto? projectContext)
```

保留当前签名作为 overload，内部传 `projectContext: null`，避免一次性破坏旧测试。

- [x] **步骤 4：追加 Project context prompt 段**

插入位置：`Task context` 之后，`Allowed MCP tools` 之前。

内容必须包含：

```text
Project context:
- version: ...
- sources: ...
- summary: ...
- relevant sections:
- tool guidance:
- risk boundaries:
- unknowns:
```

限制：

- context 为 null 时写 `Project context: unavailable`。
- summary 最大 2500 字符。
- 每个 section/tool/risk 最大 900 字符。
- 完整 prompt 必须在测试中小于 16000 字符。
- 保留原有 final JSON contract。

- [x] **步骤 5：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentPromptBuilderTests
```

预期：PASS。

- [x] **步骤 6：Commit**

```powershell
git add src\PoeStudio.Core\Agent\AgentPromptBuilder.cs tests\PoeStudio.Tests\AgentPromptBuilderTests.cs
git commit -m "feat(agent): inject project context into prompts"
```

---

## 8. 任务 6：Orchestrator preflight 与事件留痕

**文件：**
- 修改：`src/PoeStudio.Storage/Agent/AgentOrchestrator.cs`
- 修改：`src/PoeStudio.Api/Program.cs`
- 修改：`tests/PoeStudio.Tests/AgentOrchestratorTests.cs`
- 修改：`tests/PoeStudio.Tests/AgentApiSmokeTests.cs`

- [x] **步骤 1：运行影响分析**

通过 GitNexus MCP：

```text
mcp__gitnexus__impact({
  "repo": "POE-Studio",
  "target": "AgentOrchestrator",
  "direction": "upstream"
})
```

记录风险。若 HIGH 或 CRITICAL，先说明影响面再继续。

- [x] **步骤 2：编写 orchestrator 失败测试**

测试必须断言：

- `StartRunAsync` 在 Codex runner 前加载 project context。
- events 中包含 `Project context loaded`。
- event payload 包含 `projectContextLoaded: true`、`repositoryRoot`、`sources`。
- plan 中第一步是 `Load project context`。
- fake runner 捕获到的 prompt 包含 `Project context`。

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentOrchestratorTests
```

预期：FAIL。

- [x] **步骤 3：修改 AgentOrchestrator 构造函数**

新增依赖：

```csharp
private readonly AgentProjectContextService _projectContextService;
```

构造函数新增参数，并更新所有测试 helper。

- [x] **步骤 4：在 ContinueRunAsync 构建 prompt 前加载 context**

流程：

1. 先读取 `settings`，再调用 `_projectContextService.BuildAsync(run.TaskKind, run.Goal, run.ResourcePath, settings.WorkingDirectory, cancellationToken)`。
2. 构造 `AgentProjectPreflightDto`。
3. 写 `AgentEventType.PlanUpdated`，message 固定为 `Project context loaded`。
4. 初始 plan 从 `Build prompt` 改为：
   - `Load project context`
   - `Build prompt`
   - `Run Codex`
   - `Store result / Request approval`
5. 调用新签名 `_promptBuilder.Build(..., projectContext)`。

- [x] **步骤 5：Program 注册服务**

在现有 Agent DI 附近增加：

```csharp
builder.Services.AddSingleton<AgentRepositoryRootResolver>();
builder.Services.AddScoped<AgentProjectContextService>();
```

不要使用 `WorkspaceRootProvider.CurrentRoot` 作为 repository root。

- [x] **步骤 6：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentOrchestratorTests|FullyQualifiedName~AgentApiSmokeTests"
```

预期：PASS。

- [x] **步骤 7：Commit**

```powershell
git add src\PoeStudio.Storage\Agent\AgentOrchestrator.cs src\PoeStudio.Api\Program.cs tests\PoeStudio.Tests\AgentOrchestratorTests.cs tests\PoeStudio.Tests\AgentApiSmokeTests.cs
git commit -m "feat(agent): record project context preflight"
```

---

## 9. 任务 7：MCP 只读项目上下文工具

**文件：**
- 修改：`src/PoeStudio.Mcp/PoeMcpTools.cs`
- 修改：`src/PoeStudio.Core/Agent/AgentCapabilities.cs`
- 创建：`tests/PoeStudio.Tests/McpProjectContextToolTests.cs`
- 修改：`tests/PoeStudio.Tests/McpToolRegistryTests.cs`
- 修改：`tests/PoeStudio.Tests/AgentCapabilitiesTests.cs`

- [x] **步骤 1：编写 MCP 工具失败测试**

测试必须覆盖：

- `McpToolRegistry.CreateDefault()` 工具列表包含 `poe_get_project_context`。
- annotation 是 readOnly。
- 调用 `poe_get_project_context` 返回 JSON。
- 返回内容包含 summary、sources、toolGuidance、riskBoundaries。
- 缺失知识文档时返回成功，但 `unknowns` 包含 missing，不返回 MCP error。
- 返回内容不包含整篇知识底座全文；summary 和 sections 遵守 budget。

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~McpProjectContextToolTests|FullyQualifiedName~McpToolRegistryTests"
```

预期：FAIL。

- [x] **步骤 2：注册 `poe_get_project_context`**

schema 参数：

- `taskKind`: string
- `goal`: string
- `resourcePath`: string
- `repositoryRoot`: string，可选，仅测试和高级场景使用

实现要求：

- 只读。
- 不读取 overlay，不写文件。
- 使用 `AgentRepositoryRootResolver` + `AgentProjectContextService`。
- 若 arguments 提供 `repositoryRoot`，作为 explicit root。
- 若未提供，使用 resolver 默认候选；MCP 工具没有 Agent settings，因此不能假设 POE Studio workspace root 是 repository root。

- [x] **步骤 3：更新 capabilities**

所有能力都必须包含 `poe_get_project_context`：

- `question`
- `read-only-analysis`
- `datc64-translation`

更新 `AgentCapabilitiesTests` 断言每个 capability 都包含该工具。

- [x] **步骤 4：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~McpProjectContextToolTests|FullyQualifiedName~McpToolRegistryTests|FullyQualifiedName~AgentCapabilitiesTests|FullyQualifiedName~AgentPromptBuilderTests"
```

预期：PASS。

- [x] **步骤 5：Commit**

```powershell
git add src\PoeStudio.Mcp\PoeMcpTools.cs src\PoeStudio.Core\Agent\AgentCapabilities.cs tests\PoeStudio.Tests\McpProjectContextToolTests.cs tests\PoeStudio.Tests\McpToolRegistryTests.cs tests\PoeStudio.Tests\AgentCapabilitiesTests.cs
git commit -m "feat(agent): expose project context through MCP"
```

---

## 10. 任务 8：端到端行为验收

**文件：**
- 修改：`tests/PoeStudio.Tests/AgentApiSmokeTests.cs`
- 创建：`docs/superpowers/reports/2026-05-23-agent-project-cognition-integration-acceptance.md`

- [x] **步骤 1：fake Codex prompt 验证**

API smoke 或 orchestrator smoke 必须捕获 prompt，并断言：

- `Project context`
- `current working state`
- `poe_get_project_context`
- `poe_read_resource` 的读取层限制
- `Requires approval`
- prompt 总长度小于 16000 字符。
- prompt 没有携带 `docs/agent/poe-studio-project-workflows.md` 的整篇内容或任一长章节全文。

- [x] **步骤 2：fake Codex run event 验证**

创建 run 后断言：

- events 包含 `Project context loaded`。
- payload 中 `projectContextLoaded: true`。
- plan 包含 `Load project context`。
- Codex runner 失败时仍保留 project context event。

- [x] **步骤 3：真实 Codex question smoke**

启动 API：

```powershell
dotnet run --project src\PoeStudio.Api\PoeStudio.Api.csproj --urls http://localhost:5010
```

发起只读 question：

```text
请说明 POE Studio 中“当前工作态”和 MCP 读取层限制的区别，只读回答，不要写 overlay。
```

验收要求：

- run 到 `Succeeded`。
- events 中出现 `Project context loaded`。
- Codex 至少调用 `poe_get_project_context`，或最终回答明确引用项目上下文。
- 回答能区分 UI PreferOverlay/current working state 与 MCP 当前无 `useOverlay` 参数。

- [x] **步骤 4：DATC64 认知样例 smoke**

发起只读或 proposal 前置任务：

```text
我想继续翻译 data/balance/traditional chinese/activeskills.datc64。先只分析你需要确认哪些上下文，不要写 overlay。
```

验收要求：

- Agent 不直接生成写入动作。
- Agent 提到必须确认目标 profile、来源 profile、目标读取层、来源表、overlay/current working state。
- Agent 不把 DATC64 当成唯一项目能力。

- [x] **步骤 5：完整测试**

```powershell
dotnet test PoeStudio.sln --no-restore
```

预期：全部通过。

- [x] **步骤 6：写验收报告**

验收报告必须包含：

```text
Agent project cognition integration status: PASS
```

以及：

- 测试命令和结果。
- fake Codex prompt 验证摘要。
- prompt budget 验证摘要，包括捕获到的 prompt 字符数和“不含全文灌入”的断言结果。
- run event 验证摘要。
- 真实 Codex smoke 的 run id、event 摘要、最终回答摘要。
- 未解决问题。

- [x] **步骤 7：Commit**

```powershell
git add tests\PoeStudio.Tests\AgentApiSmokeTests.cs docs\superpowers\reports\2026-05-23-agent-project-cognition-integration-acceptance.md
git commit -m "docs(agent): record project cognition integration acceptance"
```

---

## 11. 完成判定

- [x] `AgentRepositoryRootResolver` 能稳定区分 repository root 与 workspace root。
- [x] `AgentProjectContextService` 能读取知识底座并生成摘要。
- [x] `AgentProjectContextSelector` 能按任务选择相关项目知识。
- [x] `AgentPromptBuilder` 每次 run prompt 包含项目上下文摘要、工具边界和风险边界。
- [x] `AgentOrchestrator` 每次 run 前记录项目上下文 preflight 事件。
- [x] MCP 新增只读 `poe_get_project_context`。
- [x] `AgentCapabilities` 让所有能力都可使用 `poe_get_project_context`。
- [x] fake Codex 测试证明 prompt 注入生效。
- [x] fake Codex 测试证明 prompt 长度受控，小于 16000 字符，且没有整篇知识底座灌入。
- [x] MCP `poe_get_project_context` 返回摘要化内容，不返回整篇知识底座全文。
- [x] API smoke 证明 run events 记录项目上下文加载。
- [x] 真实 Codex smoke 证明 Agent 知道 current working state 和 MCP 读取层限制。
- [x] 未实现 DATC64 业务修复，未新增正式 Agent Workspace UI，未新增任意 shell 工具。
- [x] `dotnet test PoeStudio.sln --no-restore` 通过。

---

## 12. 自检记录

- [x] 覆盖最初目标：Agent 成为理解项目的全量助手，而不是工具入口。
- [x] 覆盖当前关键问题：Agent 缺少完整项目知识，无法自然理解任务。
- [x] 没有跑偏到 DATC64 修复。
- [x] 没有跑偏到 Stage 3 UI。
- [x] 没有把 workspace root 当 repository root。
- [x] 没有引入 token 不可控风险；prompt budget 有测试和验收报告证据。
- [x] 所有代码行为都有计划步骤、测试和 commit。
- [x] 知识底座从文档进入运行链路，并可被 run events 追踪。

