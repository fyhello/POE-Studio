# POE Studio Agent Codex-first Planner Stage 4 验收报告

日期：2026-05-23
分支：`codex/fix-agent-oodle-inheritance`

## 范围

Stage 4 已把 Agent Workspace 的主入口改为 Codex-first Planner：

- `auto` 是请求入口，不是可执行能力。
- 后端没有新增关键词意图解析器，也没有把 `auto` 映射回 `question`。
- Planner 由 Codex 输出结构化计划；POE Studio 只做 deterministic guardrails。
- 写入型 DATC64 翻译仍只生成 approval proposal，不直接写 overlay。

Stage 4 仍不是“全能自动写代码 Agent”。本阶段只把任务理解和规划主路径改回 Codex-first，并保留能力白名单、审批和写入边界。

## 分步提交

- `05f0764 feat(agent): add codex planner plan contract`
- `a246069 feat(agent): build codex-first planner prompt`
- `1d47e3d feat(agent): validate codex planner output`
- `ef01cb2 feat(agent): run auto tasks through codex planner`
- `1c09f33 feat(agent): expose auto planner runs`
- `3189d42 feat(agent): make workspace auto planner first`
- `4900176 docs(agent): record codex-first planner stage4 acceptance`

## 复核修复

2026-05-24 复核发现两处 Stage 4 核心缺口，并已补回归测试和实现：

- `auto` retry：`RetryAsync` / `RetryShellAsync` 对 `previous.TaskKind == "auto"` 改走 `StartAutoRunShellAsync`，避免把 `auto` 传给可执行能力入口。
- resolved profile：auto Guard 通过后，执行 run、execution prompt、DATC64 parser、approval 和只读 result wrap 均使用 `guard.ProfileId`，不再回落到原始 thread/run profile。

新增覆盖：

- `Agent_run_auto_retry_from_waiting_for_input_returns_new_auto_run`
- `RetryAsync_auto_creates_new_auto_attempt_without_using_executable_task_kind_path`
- `ContinueRunAsync_auto_uses_planner_resolved_profile_for_datc64_approval`

## 测试证据

定向 Stage 4 测试：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentTaskPlanParserTests|FullyQualifiedName~AgentPlannerPromptBuilderTests|FullyQualifiedName~AgentPlanGuardServiceTests|FullyQualifiedName~AgentOrchestratorTests|FullyQualifiedName~AgentApiSmokeTests|FullyQualifiedName~FrontendAgentWorkspaceTests"
```

初始结果：`46/46 passed`

复核修复后 Orchestrator/API 定向测试：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentOrchestratorTests|FullyQualifiedName~AgentApiSmokeTests"
```

结果：`35/35 passed`

Agent/MCP/DATC64 相关回归：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~Agent|FullyQualifiedName~Mcp|FullyQualifiedName~Datc64"
```

结果：`194/194 passed`

全量测试：

```powershell
dotnet test PoeStudio.sln --no-restore
```

复核修复后全量结果：`472/472 passed`

说明：全量测试期间发现若干 Windows 进程型测试在负载下等待窗口过短，已仅调整测试超时阈值；未修改生产 MCP/Codex runner 逻辑。

## GitNexus 证据

预修改影响分析：

- `AgentRunDto`：HIGH，6 个直接调用、2 条执行流程受影响。按计划只在 record 尾部追加默认字段，保持旧 JSON/构造兼容。
- `AgentStore`：HIGH，17 个直接引用。任务 3 未修改 `AgentStore`，Guard 只读 `ProfileStore`、`ResourceIndexStore`、`OverlayStore`。
- `AgentOrchestrator`：MEDIUM，6 个直接引用。已用 `AgentOrchestratorTests` 覆盖 auto 两阶段流程。
- `/api/agent/runs`：`api_impact` 未识别该 route；补充 `MapAgentRoutes` impact 为 LOW。
- `Program.cs` impact 查询因同名文件命中 `src/PoeStudio.Mcp/Program.cs`，结果不适合作 API DI 风险证据；以 `AgentApiSmokeTests` 作为 DI 验证。

任务 7 提交前最终检测：

```text
mcp__gitnexus__detect_changes({ "repo": "POE-Studio", "scope": "all" })
```

结果：`risk_level = low`，`changed_count = 0`，`affected_count = 0`，`changed_files = 1`，`changed_symbols = []`，`affected_processes = []`。  
说明任务 1-6 已分步提交，最终报告提交前工作树只剩文档/计划更新，没有触及已索引运行符号或执行流程。

2026-05-24 复核修复提交前检测：

```text
mcp__gitnexus__detect_changes({ "repo": "POE-Studio", "scope": "all" })
```

结果：`risk_level = low`，`changed_count = 22`，`affected_count = 0`，`changed_files = 7`，`affected_processes = []`。  
说明：变更集中在 `AgentOrchestrator` auto retry/profile 修复、对应 API/Orchestrator 回归测试、进程型测试等待窗口稳定化和本验收报告；未检测到受影响执行流程。

## 实机验收

### Fake executable 业务链路验收

验收方式：启动真实 ASP.NET Core API，经真实 HTTP API、后台 run、AgentStore 文件持久化、Planner/Guard/approval 流程执行。Codex 外部输出使用 deterministic fake executable 替代；因此这只能证明 POE Studio 业务链路，不证明真实 Codex 自然语言感知效果。

验收 workspace：

`C:\Users\25147\AppData\Local\Temp\poe-stage4-accept-6c67ff18f2b54343bfea06c62fc4bff1`

#### 1. 自然语言翻译

- threadId：`thread-05903639481e436ea7aa2244c27e43bd`
- runId：`run-0713c0576c284aada3b3a6679a039be0`
- requestedTaskKind：`auto`
- taskKind：`auto`
- resolvedTaskKind：`datc64-translation`
- status：`WaitingForApproval`
- approvalCount：`1`
- events：`Planner completed = 1`，`Plan guard passed = 1`

Planner 摘要：`Translate the selected DATC64 table.`  
Guard 摘要：OK，resourcePath 为 `data/balance/traditional chinese/activeskills.datc64`，warning 为 `Oodle path is not configured; execution may fail if DATC64 decoding requires it.`

说明：真实 workspace 中 `国际服-目标` profile 和 `activeskills.datc64` 索引存在，但 profile 当前 Oodle 状态为 Missing；计划要求“Oodle 已配置”的前置条件未满足。Guard 按设计对空 Oodle 给 warning、不阻断。

#### 2. 缺资源澄清

- threadId：`thread-09bb8a82d5ac4612bd9cabbea8f05d9e`
- runId：`run-cf92daad6cf14fae9456f8c2e5d3b60d`
- status：`WaitingForInput`
- approvalCount：`0`
- clarification：`Select a DATC64 resource before translation.`

验收结果：没有 DATC64 proposal，没有 overlay 写入。

#### 3. 普通项目问题

- threadId：`thread-426fa0cd30d14fe9be01fd60dba72a3a`
- runId：`run-532620de831244a2b2eb7e49a158fa4c`
- requestedTaskKind：`auto`
- resolvedTaskKind：`question`
- status：`Succeeded`
- events：`Planner completed = 1`，`Plan guard passed = 1`

Result 摘要：`Current working state is the editable project/runtime state; MCP read layer exposes bounded read-only context and tools.`

### Real Codex CLI Planner 感知验收

命令：

```powershell
codex exec --json --sandbox read-only -C . --output-last-message <temp-file> -
```

输入为只读 Planner prompt，要求根据自然语言问题 `POE Studio 当前工作态和 MCP 读取层有什么区别？` 输出 Stage 4 plan JSON。

结果：

- Codex CLI：`codex-cli 0.131.0`
- threadId：`019e57ce-0804-7eb2-b534-bfc16214c3f2`
- last message：输出 fenced JSON，`requestedTaskKind = auto`，`resolvedTaskKind = question`，`profileId = profile-1`，`requiredApprovals = []`
- 环境噪声：remote plugin sync 401、plugin load warning
- Windows sandbox 限制：Codex 尝试读取 `using-superpowers` skill 时命令执行失败，错误为 `windows sandbox: runner error: CreateProcessAsUserW failed: 5`

结论：真实 Codex CLI 能返回符合 Planner schema 的结构化判断，但本次真实验收仍受 Windows sandbox/plugin 认证噪声影响；不能把 fake executable 的三条业务链路验收表述为真实 Codex-first 感知全量 PASS。

## 结论

复核指出的问题已修复并有回归测试覆盖。自然语言入口现在默认走 `auto` -> Codex Planner -> Guard -> resolved capability；`auto` retry 可恢复；Planner resolved profile 会进入执行链路；缺上下文会进入 `WaitingForInput`；写入型 DATC64 仍停在 approval，不会自动写 overlay。

验收证据边界：fake executable 三例只算业务链路验收；real Codex CLI 只读 Planner smoke 可产出结构化 plan，但仍记录 sandbox/plugin 环境限制。
