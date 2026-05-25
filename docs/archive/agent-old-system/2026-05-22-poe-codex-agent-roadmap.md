# POE Studio Codex Agent 1+2+3 总计划 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 分 1→2→3 三个阶段把 Codex 能力接入 POE Studio，先交付可被 Codex 调用的 POE MCP 工具，再交付后端 Agent 运行时，最后交付 IDE-like Agent Workspace UI。

**架构：** Stage 1 只构建独立 `PoeStudio.Mcp` stdio MCP 服务器，让 Codex CLI 能真实发现和调用 POE Studio 项目上下文工具。Stage 2 才在 POE Studio 后端接入 Codex 会话、事件、审批、计划与进度存储。Stage 3 才建设前端 Agent 工作台；任何阶段不得把普通脚本入口、按钮面板或静态表单冒充为 Agent。

**技术栈：** .NET 8、ASP.NET Core Minimal API、xUnit、Model Context Protocol stdio、官方 ModelContextProtocol C# SDK（Stage 1 优先）、Codex CLI、POE Studio 现有 Core/Storage/Contracts。

---

## 0. 固定硬约束

- [ ] **H0.1：计划先行**  
  任何代码创建、代码编辑、脚本创建、会改变项目状态的脚本执行、迁移、批量操作、自动化操作，都必须先有本计划或阶段计划中的复选框步骤对应。没有对应步骤时，先更新计划并记录原因。

- [ ] **H0.2：进度可追踪**  
  执行者必须在计划文件或执行记录中更新每个任务状态。每次阶段验收必须记录 `PASS` / `FAIL`、命令、输出摘要、失败原因和下一步。

- [ ] **H0.3：行为可追溯**  
  每个代码行为必须能追溯到计划文件、任务编号、测试编号或明确批准点。高风险动作必须先写明风险和批准点。

- [ ] **H0.4：禁止伪 Agent**  
  只有同时具备「LLM 自主推理循环」「可发现和调用的工具」「上下文读写」「审批门禁」「任务/计划/进度持久化」「失败可恢复或可审计事件」时，才允许在产品内称为 Agent。Stage 1 只能称为 `POE Studio MCP Tools`，不能称为完整 Agent。

- [ ] **H0.5：阶段门禁**  
  Stage 1 未验收 `PASS` 前禁止启动 Stage 2。Stage 2 未验收 `PASS` 前禁止启动 Stage 3。不得跨阶段提前做 UI、后端 Agent 路由、Codex app-server 封装或真实 overlay 写入。

- [ ] **H0.6：当前干净基线说明**  
  当前 `main` 分支没有旧 Agent 代码、没有 `/api/agent/*`、没有 `docs/agent-hard-constraints.md`、没有 `docs/ai-project-memory.md`。计划执行者不得引用这些不存在的文件作为实现前提。

---

## 1. 阶段边界

### Stage 1：POE MCP Tools

**目标：** Codex CLI 能通过 MCP stdio 调用 POE Studio 项目工具，读取 profile、workspace、索引状态、资源搜索、资源读取、DATC64 可翻译单元提取。  
**详细计划：** `docs/superpowers/plans/2026-05-22-poe-mcp-stage1.md`

**允许：**
- [ ] 新增 `src/PoeStudio.Mcp` console 项目。
- [ ] 新增 MCP 协议和工具注册测试。
- [ ] 优先使用官方 ModelContextProtocol C# SDK 实现 stdio server；只有 SDK 无法满足当前 Codex CLI stdio 接入时，才允许按 Stage 1 计划中的 fallback 条件手写协议。
- [ ] 复用现有 `PoeStudio.Core`、`PoeStudio.Storage`、`PoeStudio.Contracts`。
- [ ] 提供只读或 dry-run 工具。
- [ ] 用 Codex CLI 真实 smoke test 验证工具可发现、可调用。

**禁止：**
- [ ] 禁止新增 `/api/codex/*`、`/api/agent/*`。
- [ ] 禁止新增 Agent Workspace UI。
- [ ] 禁止调用 Codex app-server。
- [ ] 禁止真实写 overlay 草稿、应用草稿、批量修改文件。
- [ ] 禁止加入自动工具生成器或可执行任意脚本的 ToolBuilder。

**Stage 1 验收门槛：**
- [ ] `dotnet test PoeStudio.sln --no-restore` 通过。
- [ ] `codex mcp add poe-studio -- dotnet run --project src\PoeStudio.Mcp\PoeStudio.Mcp.csproj -- --workspace-root "<workspace>"` 成功。
- [ ] `codex mcp list` 能看到 `poe-studio`。
- [ ] `codex exec --json -C . "使用 POE Studio MCP 工具列出 profile 和索引状态，不要写入任何文件。"` 的 JSON 事件中能证明 Codex 调用了 POE MCP 工具。
- [ ] DATC64 提取工具返回可翻译单元样例。
- [ ] 资源读取边界被验收：physical resource 能读；native Bundles2 resource 如 Stage 1 未抽取只读读取服务，必须返回明确 `isError: true`，不能伪造成功。
- [ ] 验收记录证明没有产生 overlay 写入、没有新增 draft、没有修改资源文件。

### Stage 2：POE Codex Bridge

**启动条件：** Stage 1 验收记录为 `Stage 1 status: PASS`，且用户明确批准进入 Stage 2。

**目标：** POE Studio 后端托管或连接 Codex 运行时，形成真正的 Agent 会话后端：任务、计划、进度、工具调用、审批、事件流、失败恢复、审计日志。

**允许：**
- [ ] 新增后端 Agent 域模型：thread、run、event、approval、tool call、plan、progress。
- [ ] 新增 `/api/codex/*` 或 `/api/agent/*`，但必须先做 route impact analysis。
- [ ] 后端调用 `codex exec --json` 或验证后的 Codex app-server 接口。
- [ ] 把 Stage 1 MCP 工具配置为 Codex 可用工具。
- [ ] 新增真实 overlay 写入能力，但必须走审批门禁。

**禁止：**
- [ ] 禁止未持久化计划和事件就执行写操作。
- [ ] 禁止模型配置每次刷新丢失。
- [ ] 禁止用户提出任务后只返回“需要添加索引”而不提供可执行下一步和工具链决策。
- [ ] 禁止把 Codex CLI 一次性命令包装成无状态按钮。

**Stage 2 验收门槛：**
- [ ] 用户在 POE Studio 发起一个 DATC64 翻译任务。
- [ ] 后端创建任务计划并持久化。
- [ ] Codex 通过 MCP 工具读取上下文、提取 DATC64 单元、生成翻译候选。
- [ ] 写入前必须产生审批事件，用户批准后才写入 draft。
- [ ] 刷新页面后模型设置、会话、计划、进度、审批状态仍然存在。
- [ ] 失败任务可以查看原因，可以重试，不会静默丢失。

### Stage 3：IDE-like Agent Workspace UI

**启动条件：** Stage 2 验收记录为 `Stage 2 status: PASS`，且用户明确批准进入 Stage 3。

**目标：** 建设类似 Codex / IDE 插件体验的 Agent 工作台，而不是普通表单页面。用户输入目标，Agent 展示计划、工具调用、文件差异、审批点、执行状态和最终结果。

**允许：**
- [ ] 新增 Agent Workspace 前端视图。
- [ ] 新增计划面板、事件时间线、工具调用详情、审批对话框、diff/review 面板、模型与权限设置。
- [ ] 支持 DATC64 翻译任务作为第一个完整端到端体验。
- [ ] 预留后续全量项目助手能力入口。

**禁止：**
- [ ] 禁止用静态 prompt 表单冒充 Agent。
- [ ] 禁止每次刷新丢失设置。
- [ ] 禁止隐藏工具调用和失败原因。
- [ ] 禁止 UI 先行导致后端 Agent 能力缺失。

**Stage 3 验收门槛：**
- [ ] 用户只输入自然语言目标即可启动任务。
- [ ] UI 显示 Agent 计划、正在执行步骤、已调用工具、审批请求、失败/重试、最终结果。
- [ ] DATC64 翻译任务可从提取、生成候选、人工审核、批准写入 draft 走完整闭环。
- [ ] 刷新页面后当前会话和设置不丢。

---

## 2. 总任务清单

### 任务 1：执行 Stage 1 详细计划

**文件：**
- 执行：`docs/superpowers/plans/2026-05-22-poe-mcp-stage1.md`
- 创建：`src/PoeStudio.Mcp/PoeStudio.Mcp.csproj`
- 创建：`src/PoeStudio.Mcp/Program.cs`
- 创建：`src/PoeStudio.Mcp/McpProtocol.cs`
- 创建：`src/PoeStudio.Mcp/McpToolRegistry.cs`
- 创建：`src/PoeStudio.Mcp/PoeWorkspaceResolver.cs`
- 创建：`src/PoeStudio.Mcp/PoeMcpTools.cs`
- 修改：`PoeStudio.sln`
- 修改：`tests/PoeStudio.Tests/PoeStudio.Tests.csproj`
- 创建：`tests/PoeStudio.Tests/McpProtocolTests.cs`
- 创建：`tests/PoeStudio.Tests/McpToolRegistryTests.cs`
- 创建：`tests/PoeStudio.Tests/McpWorkspaceResolverTests.cs`
- 创建：`tests/PoeStudio.Tests/McpDatc64ToolTests.cs`
- 创建：`docs/superpowers/reports/2026-05-22-poe-mcp-stage1-acceptance.md`

- [ ] **步骤 1：按 Stage 1 计划执行所有任务**  
  执行者必须逐项更新 `2026-05-22-poe-mcp-stage1.md` 中的复选框，不得跳过测试步骤。

- [ ] **步骤 2：提交 Stage 1 验收报告**  
  验收报告必须包含 `Stage 1 status: PASS` 或 `Stage 1 status: FAIL`，并附命令和输出摘要。

- [ ] **步骤 3：Stage 1 通过后停止**  
  Stage 1 完成后必须停止并等待用户批准进入 Stage 2，不得继续实现 Stage 2。

### 任务 2：Stage 2 计划冻结

**文件：**
- 创建：`docs/superpowers/plans/YYYY-MM-DD-poe-codex-bridge-stage2.md`

- [ ] **步骤 1：基于 Stage 1 验收结果编写 Stage 2 详细计划**  
  计划必须具体到文件、路由、模型、测试、审批门禁、失败恢复和持久化结构。

- [ ] **步骤 2：用户批准后才能执行 Stage 2**  
  执行前记录批准点：`Approval: enter Stage 2, approved by user, date/time`。

### 任务 3：Stage 3 计划冻结

**文件：**
- 创建：`docs/superpowers/plans/YYYY-MM-DD-poe-agent-workspace-stage3.md`

- [ ] **步骤 1：基于 Stage 2 验收结果编写 Stage 3 详细计划**  
  计划必须具体到前端状态、API 契约、刷新恢复、审批交互、工具调用展示和端到端 UI 验收。

- [ ] **步骤 2：用户批准后才能执行 Stage 3**  
  执行前记录批准点：`Approval: enter Stage 3, approved by user, date/time`。

---

## 3. 跑偏防线

- [ ] **R1：如果实现者开始做 UI，立即停止**  
  Stage 1 没有 UI 任务。任何 UI 文件变更都判定为跑偏。

- [ ] **R2：如果实现者新增 `/api/agent/*` 或 `/api/codex/*`，立即停止**  
  这些属于 Stage 2，不允许在 Stage 1 出现。

- [ ] **R3：如果实现者新增真实写入工具，立即停止**  
  Stage 1 只读或 dry-run。真实 draft 写入必须等 Stage 2 审批门禁。

- [ ] **R4：如果实现者只写脚本而没有 MCP 协议，立即停止**  
  Stage 1 的交付物是 Codex 可发现和调用的 MCP tools，不是命令行脚本集合。

- [ ] **R5：如果验收没有 Codex CLI 真实调用证据，Stage 1 不能 PASS**  
  单元测试通过不等于 Codex 接入成功。

---

## 4. 自检记录

- [ ] 覆盖用户总目标：全能项目助手方向，不是 DATC64 专用脚本。
- [ ] 覆盖现阶段目标：先做 Codex 可调用的 POE MCP 工具。
- [ ] 覆盖硬要求：计划先行、进度可跟踪、行为可追溯。
- [ ] 覆盖阶段门禁：Stage 1 PASS 前不得进入 Stage 2。
- [ ] 覆盖历史错误：不做伪 Agent、不做静态 UI、不做无状态工具入口、不做未审批写入。
- [ ] 覆盖验收证据：真实 Codex CLI MCP 调用、DATC64 提取样例、无写入证明。
