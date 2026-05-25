# POE Studio Agent Knowledge Contract v0.1 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 把 POE Studio 现有 Agent 知识底座从“散落文档 + prompt 片段 + 工具描述”升级为 Codex-first 的运行时知识契约，使 Codex 能按需读取项目语义、当前工作态规则、工具边界和失败学习规则，而不是退化成脚本入口集合。

**架构：** POE Studio 继续保持薄桥接：不恢复后端关键词 Planner，不把 Codex 降级成普通 LLM 执行器。POE Studio 提供短核心契约、知识块索引、按需 MCP 知识读取工具、task frame 提示协议、工具适配自检规则和实机验收矩阵；Codex 仍负责自然语言理解、工具选择、执行组合和缺口诊断。

**技术栈：** Markdown/JSON 知识契约、ASP.NET Core Minimal API、POE Studio MCP stdio tools、Codex CLI `exec --json`、原生 JavaScript SSE UI、xUnit、GitNexus 影响分析。

---

## 0. 硬约束

- [ ] **H0.1：不得恢复自建 Planner/Guard/Orchestrator。** 禁止用后端关键词分类、固定脚本路由或旧 Agent 编排层替代 Codex 判断。
- [ ] **H0.2：不得全文注入知识底座。** `ChatService` prompt 只允许注入短核心契约、`currentViewContextId` 和知识索引摘要；详细知识必须通过 MCP 按需读取。
- [ ] **H0.3：核心契约必须短小稳定。** `core-contract.md` 第一版目标 500-1000 token，只放项目硬语义，不放长篇工作流。
- [ ] **H0.4：source/target 语义必须从语言方向中解耦。** `source/current source` 是参考来源，`target/current target` 是可编辑目标和 overlay 写入目标；不得从 profile name 或 resource path 推断用户期望输出语言。
- [ ] **H0.5：current-view 优先级不可回退。** 用户说“当前表格 / 当前草稿 / 当前对比 / 已打开表格”时，必须先使用 current-view 快照工具，不得默认 raw DATC64/Oodle reread。
- [ ] **H0.6：写入边界不可放松。** 业务写入只能进入 target profile 的 overlay staging；source/reference 永远不可被修改，除非用户明确切换编辑目标。
- [ ] **H0.7：工具适配必须自检。** Codex 调工具前必须判断工具语义是否回答用户任务；工具返回 0 不能自动等于“没有问题”。
- [ ] **H0.8：用户纠错必须可沉淀。** 用户指出 Agent 误解项目语义时，必须能归因为知识缺失、工具语义不匹配、current-view 误读、写入边界不清或 prompt 误导，并转化为知识/测试/工具更新。
- [ ] **H0.9：所有代码行为必须 TDD。** 每个实现任务先写失败测试并运行确认失败，再做最小实现，再运行验证。
- [ ] **H0.10：每个实现任务完成后单独提交。** commit message 必须引用任务编号；提交前必须运行 `gitnexus_detect_changes()`。
- [ ] **H0.11：实机验收优先于单元测试。** 最终必须通过真实 UI、真实 Codex、真实 MCP、真实 current table 工作流验收，不能只用单元测试宣称完成。
- [ ] **H0.12：知识索引不得变成路由表。** `index.json` 只能描述知识块语义、适用边界和检索提示；禁止写“用户说某句话就必须调用某工具”的固定脚本映射。
- [ ] **H0.13：任务理解必须可追踪。** 第一版允许 Codex 不向用户展示完整 task frame，但 run trace 必须能记录 task frame 摘要、tool-fit 判断和 capability-gap 归因，供诊断和后续学习使用。

---

## 1. 当前问题和根因

### 1.1 现有知识入口

现有知识底座文件：

- `docs/agent/poe-studio-agent-context.md`
- `docs/agent/poe-studio-project-workflows.md`
- `docs/ai-project-memory.md`

现有运行时知识入口：

- `src/PoeStudio.Api/ChatService.cs` 拼接 session prompt。
- `src/PoeStudio.Mcp/PoeMcpTools.cs` 的 `poe_get_project_overview` 返回项目摘要、工具指导和 common workflows。
- MCP tool descriptions 提供工具级能力说明。
- `/api/chat` current-view 快照提供当前 UI 状态。

### 1.2 已暴露问题

- Agent 把 `source/target` 错当成翻译方向，而不是“参考 / 编辑目标”。
- 旧工具 `poe_find_current_table_untranslated_cells` 返回 0 时，Agent 直接下结论，但该工具并不检测“目标仍含繁中/未转简中”。
- 修复时容易散落在 `ChatService` prompt、`poe_get_project_overview`、工具描述和前端摘要中，长期会变成脚本入口集合。
- 现有知识文档是人可读文档，不是 Agent 每次 run 都能稳定消费和测试保护的运行时契约。

### 1.3 本计划解决边界

本计划解决：

- 知识底座结构化。
- 按需 MCP 知识检索。
- ChatService 从“堆规则”改为“注入核心契约 + 知识索引”。
- Codex task frame 和 tool-fit 自检协议。
- task frame、tool-fit 和 capability-gap 的 trace 记录。
- 用户纠错到系统资产的学习闭环。
- 真实 UI 验收矩阵。

本计划不解决：

- 完整自动翻译质量。
- 完整 OpenCC 繁简转换。
- 批量写入所有 DATC64 单元格。
- 恢复旧 Stage 4 Agent Workspace 或旧 Planner。

---

## 2. 文件结构

### 新增文件

- `docs/agent/knowledge/core-contract.md`
  - 常驻核心项目语义契约，短小、稳定、可全文注入 prompt。
- `docs/agent/knowledge/index.json`
  - 知识块索引，列出 sectionId、title、summary、file、keywords、appliesWhen、priority。
- `docs/agent/knowledge/workflows/current-view.md`
  - 当前 UI 工作态、currentViewContextId、current-view 工具优先级。
- `docs/agent/knowledge/workflows/datc64-translation.md`
  - DATC64 作为样例工作流：source/reference、target/editable、可编辑列、读层、写 overlay。
- `docs/agent/knowledge/workflows/overlay-staging.md`
  - overlay staging 写入、审核、revert、patch build 边界。
- `docs/agent/knowledge/workflows/resource-indexing.md`
  - profile、resource index、raw read、Native/GGPK/Oodle 读取边界。
- `docs/agent/knowledge/diagnostics/tool-fit-and-capability-gap.md`
  - 工具适配自检、工具语义不匹配、能力缺口报告规则。
- `docs/agent/knowledge/diagnostics/learning-loop-v0.2.md`
  - 记录 v0.2 学习事件存储和验收回放接续边界，避免 v0.1 假装完成完整自学习。
- `src/PoeStudio.Contracts/AgentKnowledgeDtos.cs`
  - MCP 知识工具返回 DTO 和知识索引 DTO。
- `src/PoeStudio.Contracts/AgentTaskFrameDtos.cs`
  - 记录 task frame 摘要、tool-fit 判断和 capability-gap 事件的 trace DTO。
- `src/PoeStudio.Storage/Agent/AgentKnowledgeStore.cs`
  - 从 repo docs 读取知识索引和 section 内容，限制路径和 maxBytes。
- `tests/PoeStudio.Tests/AgentKnowledgeStoreTests.cs`
  - 验证索引读取、section 读取、未知 section、maxBytes、安全路径。
- `tests/PoeStudio.Tests/AgentTaskFrameTraceTests.cs`
  - 验证 task frame / tool-fit / capability-gap 可以写入 run trace，供诊断读取。

### 修改文件

- `src/PoeStudio.Mcp/PoeMcpTools.cs`
  - 新增 `poe_get_project_knowledge`。
  - 收缩 `poe_get_project_overview` 为项目摘要 + 核心规则摘要 + knowledgeIndex。
- `tests/PoeStudio.Tests/McpToolRegistryTests.cs`
  - 注册和只读标记覆盖 `poe_get_project_knowledge`。
- `tests/PoeStudio.Tests/McpPoeToolsTests.cs`
  - 覆盖 overview 返回知识目录而不是长篇百科；覆盖 knowledge 工具按 section 返回内容。
- `src/PoeStudio.Api/ChatService.cs`
  - prompt 只注入 core contract summary、knowledge index hint、currentViewContextId 和 task-frame/tool-fit 指令。
  - 将 Codex 明确输出的 task frame / tool-fit / capability-gap JSON 事件写入 run trace。
- `tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs`
  - 覆盖 prompt 不再散落长规则，而是指向 knowledge contract 和 task frame。
  - 覆盖 task frame / capability-gap 事件能进入 trace。
- `src/PoeStudio.Api/wwwroot/app.js`
  - 工具结果摘要支持 `poe_get_project_knowledge`。
- `tests/PoeStudio.Tests/FrontendDatc64WorkflowTests.cs`
  - 静态覆盖新工具结果摘要。
- `docs/agent/poe-studio-agent-context.md`
  - 增加指向 `docs/agent/knowledge/index.json` 的权威入口说明，避免双重事实源。
- `docs/agent/poe-studio-project-workflows.md`
  - 增加“长文档是来源材料，运行时以 knowledge contract 为准”的说明。

---

## 3. 数据契约

### 3.1 知识索引 JSON

`docs/agent/knowledge/index.json` 第一版必须使用以下结构：

```json
{
  "version": "0.1",
  "updatedAt": "2026-05-25",
  "coreSectionId": "core.contract",
  "sections": [
    {
      "sectionId": "core.contract",
      "title": "Core Agent Contract",
      "summary": "Always-on POE Studio Agent semantics.",
      "file": "docs/agent/knowledge/core-contract.md",
      "keywords": ["source", "target", "current-view", "overlay", "tool-fit"],
      "appliesWhen": ["every agent run"],
      "priority": 100
    }
  ]
}
```

### 3.2 MCP 知识工具输入

`poe_get_project_knowledge` schema：

```json
{
  "sectionIds": ["core.contract", "workflow.current-view"],
  "maxBytes": 12000
}
```

规则：

- `sectionIds` 必填，最多 5 个。
- `maxBytes` 默认 12000，范围 1000-24000。
- 返回内容总量超过 `maxBytes` 时按 section 顺序裁剪，并标记 `truncated: true`。
- 只能读取 `docs/agent/knowledge/index.json` 中登记的文件。
- 禁止任意 path 参数。

### 3.3 MCP 知识工具输出

```json
{
  "version": "0.1",
  "requestedSectionIds": ["core.contract"],
  "sections": [
    {
      "sectionId": "core.contract",
      "title": "Core Agent Contract",
      "summary": "Always-on POE Studio Agent semantics.",
      "sourceFile": "docs/agent/knowledge/core-contract.md",
      "content": "Markdown content capped by maxBytes.",
      "truncated": false
    }
  ],
  "missingSectionIds": [],
  "totalBytes": 900
}
```

### 3.4 Task Frame 协议

Codex 应在每次业务执行前形成内部 task frame。第一版不要求前端展示完整 JSON，但 prompt 和 tests 必须包含这些字段名：

```json
{
  "userGoal": "",
  "currentState": "tableComparison | table | resourcePreview | overlayList | patchBuild | unknown",
  "reference": "",
  "editableTarget": "",
  "desiredOutputLanguage": "",
  "writeIntent": "read-only | staging-write | unknown",
  "preferredContext": "current-view | overlay | raw-resource | unknown",
  "requiredKnowledge": [],
  "toolFitCheck": ""
}
```

### 3.5 Task Frame Trace Event

第一版不要求 Codex 每次都把完整 task frame 暴露给用户，但当 Codex 输出以下 JSON 片段时，`ChatService` 必须写入 run trace：

```json
{
  "type": "agent_task_frame",
  "taskFrame": {
    "userGoal": "",
    "currentState": "tableComparison",
    "reference": "current source table",
    "editableTarget": "current target table",
    "desiredOutputLanguage": "Simplified Chinese",
    "writeIntent": "read-only",
    "preferredContext": "current-view",
    "requiredKnowledge": ["core.contract", "workflow.current-view"],
    "toolFitCheck": "Need a non-simplified target-cell detector."
  }
}
```

Trace event requirements:

- `eventName`: `task_frame`
- `status`: `observed`
- `DataJson`: compact JSON containing `taskFrame`

Capability gap event:

```json
{
  "type": "agent_capability_gap",
  "failureType": "tool_semantics_mismatch",
  "userGoal": "check target cells still containing Traditional Chinese",
  "missingCapability": "current-view target-cell Traditional Chinese detector",
  "proposedNextAction": "add or use poe_find_current_table_non_simplified_chinese_cells"
}
```

Trace event requirements:

- `eventName`: `capability_gap`
- `status`: `observed`
- `DataJson`: compact JSON containing the gap details

---

## 4. 实机验收矩阵

### 场景 A：当前表格繁中未转简中检查

准备：

- 打开 POE Studio：`http://localhost:5010`。
- 选择目标 profile：`75c5bef9860a45658cbb2a41aae5c057`。
- 打开目标表：`data/balance/traditional chinese/activeskills.datc64`。
- 确认来源参考表：`data/balance/simplified chinese/activeskills.datc64`。
- 当前表中第 16 行 `Description @16` 仍含繁中，例如 `變形`、`燃燒`、`兇獸`、`隨後`、`創造`、`額外`、`縫`。

输入：

```text
检查当前表格中还没有翻译成简中内容的繁中单元格
```

预期工具链：

1. `poe_get_current_view_context`
2. `poe_get_project_knowledge`，section 包含：
   - `core.contract`
   - `workflow.current-view`
   - `workflow.datc64-translation`
   - `diagnostics.tool-fit-and-capability-gap`
3. `poe_find_current_table_non_simplified_chinese_cells`

预期回答效果：

- 明确当前 source 是参考表，target 是编辑目标。
- 不要求用户切换 source/target。
- 不从 `traditional chinese` 路径推断目标语言。
- 报告第 16 行 `Description @16` 是未转简中候选。
- 不写 overlay，因为用户只要求检查。

### 场景 B：工具语义不匹配自检

输入：

```text
为什么你刚才说没有漏翻，但我看到目标表里还有繁中？
```

预期效果：

- Agent 读取 run trace 或基于当前上下文解释：旧 `poe_find_current_table_untranslated_cells` 检查的是空白/英文/疑似未翻译，不等于检查繁中未转简中。
- Agent 将问题归因为 `tool_semantics_mismatch`。
- Agent 使用或建议使用 `poe_find_current_table_non_simplified_chinese_cells`。
- 不重复用旧工具返回 0 来压用户判断。
- run trace 中出现 `capability_gap` 或 `task_frame` 事件，能证明 Agent 不是口头道歉，而是记录了工具适配判断。

### 场景 C：写入边界

输入：

```text
把这些繁中单元格改成简中
```

预期效果：

- Agent 先列出 proposal，说明将写入 target profile overlay staging。
- Agent 不修改 source/reference。
- Agent 不直接改游戏原始文件。
- 写入后提示用户在 overlay 面板审核。
- 如果需要批量写入 DATC64 binary overlay，而现有工具不足，Agent 必须报告 capability gap 并请求批准补工具。

### 场景 D：知识按需读取

输入：

```text
帮我检查 patch build 为什么失败
```

预期工具链：

- `poe_get_project_knowledge` 读取 patch/overlay 相关 section。
- 不读取 `workflow.datc64-translation`，除非错误与 DATC64 overlay 有关。
- 读取 logs / overlays / readiness 相关 MCP 工具。

预期效果：

- token 不被 DATC64 长知识污染。
- 回答聚焦 patch build 失败原因和下一步检查。

---

## 任务 1：创建知识契约文件

**文件：**

- 创建：`docs/agent/knowledge/core-contract.md`
- 创建：`docs/agent/knowledge/index.json`
- 创建：`docs/agent/knowledge/workflows/current-view.md`
- 创建：`docs/agent/knowledge/workflows/datc64-translation.md`
- 创建：`docs/agent/knowledge/workflows/overlay-staging.md`
- 创建：`docs/agent/knowledge/workflows/resource-indexing.md`
- 创建：`docs/agent/knowledge/diagnostics/tool-fit-and-capability-gap.md`
- 创建：`docs/agent/knowledge/diagnostics/learning-loop-v0.2.md`
- 修改：`docs/agent/poe-studio-agent-context.md`
- 修改：`docs/agent/poe-studio-project-workflows.md`

- [x] **步骤 1：编写 core contract**

创建 `docs/agent/knowledge/core-contract.md`：

```markdown
# Core Agent Contract

This file is the always-on POE Studio Agent contract. It must stay short.

## Always-On Semantics

- POE Studio is a local Path of Exile 2 modding workbench, not a single translation script.
- Codex is the planning and tool-selection brain. POE Studio supplies project state, tools, safety boundaries, and trace evidence.
- `source` / `current source` means reference input for comparison.
- `target` / `current target` means editable target and overlay write target.
- Do not infer the user's desired output language from profile names or resource paths.
- Current table, current draft, opened table, and current comparison refer to current UI state first.
- For current UI state, call current-view tools before raw resource tools.
- Raw DATC64/resource tools read base/indexed resources unless their tool contract explicitly says overlay/current-view.
- All writes go to target overlay staging first. Source/reference is read-only by default.
- If a tool result does not answer the user's task, diagnose tool mismatch or capability gap before answering.
- If existing tools are insufficient, explain the gap and ask for approval before changing POE Studio code or adding tools.
```

- [x] **步骤 2：编写 current-view 知识块**

创建 `docs/agent/knowledge/workflows/current-view.md`：

```markdown
# Current View Workflow

## Meaning

When the user says current table, current draft, opened table, or current comparison, use the UI snapshot first.

## Required Tools

- `poe_get_current_view_context`
- Current-table analysis tools that explicitly read current-view snapshots.

## Rules

- Do not reread raw DATC64/Oodle resources before inspecting current-view when `currentViewContextId` exists.
- Current-view target rows are the editable target state currently visible in UI.
- Current-view source rows are reference rows for comparison.
- A current-view read-only check must not write overlay.
```

- [x] **步骤 3：编写 DATC64 翻译知识块**

创建 `docs/agent/knowledge/workflows/datc64-translation.md`：

```markdown
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
```

- [x] **步骤 4：编写 overlay、resource、diagnostics 知识块**

创建 `overlay-staging.md`、`resource-indexing.md`、`tool-fit-and-capability-gap.md`，内容必须覆盖：

```markdown
overlay-staging.md:
- overlay staging is draft write layer
- user reviews overlays before patch build
- write tools never directly modify base game files
- list/revert overlays are review operations

resource-indexing.md:
- profile is client/workspace context, not task intent
- resource path is virtual path
- raw read tools are base/index reads unless explicitly overlay-aware
- Native/GGPK/Oodle errors are dependency/read-layer issues, not automatic task failure

tool-fit-and-capability-gap.md:
- before calling a tool, compare tool semantics with user goal
- a successful tool call can still be the wrong tool
- zero candidates means zero according to that tool's detector only
- if tool semantics do not match, choose a better tool or report capability_gap
- user correction should be classified and converted to knowledge/test/tool updates

learning-loop-v0.2.md:
- v0.1 records task_frame and capability_gap trace events but does not claim full self-learning
- v0.2 must add persistent learning event storage or extend AgentRunTraceStore with queryable learning events
- v0.2 must add an acceptance replay harness for current-view and tool-mismatch scenarios
- user corrections must become proposed knowledge/test/tool changes after approval
```

- [x] **步骤 5：编写 index.json**

创建 `docs/agent/knowledge/index.json`，包含至少这些 section：

```json
{
  "version": "0.1",
  "updatedAt": "2026-05-25",
  "coreSectionId": "core.contract",
  "sections": [
    {
      "sectionId": "core.contract",
      "title": "Core Agent Contract",
      "summary": "Always-on POE Studio Agent semantics.",
      "file": "docs/agent/knowledge/core-contract.md",
      "keywords": ["source", "target", "current-view", "overlay", "tool-fit"],
      "appliesWhen": ["every agent run"],
      "priority": 100
    },
    {
      "sectionId": "workflow.current-view",
      "title": "Current View Workflow",
      "summary": "How Codex should use current UI snapshots before raw reads.",
      "file": "docs/agent/knowledge/workflows/current-view.md",
      "keywords": ["current table", "current draft", "current comparison", "currentViewContextId"],
      "appliesWhen": ["user refers to current UI state"],
      "priority": 90
    },
    {
      "sectionId": "workflow.datc64-translation",
      "title": "DATC64 Translation Workflow",
      "summary": "DATC64 source/reference and target/editable semantics.",
      "file": "docs/agent/knowledge/workflows/datc64-translation.md",
      "keywords": ["DATC64", "translation", "Simplified Chinese", "Traditional Chinese"],
      "appliesWhen": ["user asks about DATC64 translation or current table language conversion"],
      "priority": 80
    },
    {
      "sectionId": "workflow.overlay-staging",
      "title": "Overlay Staging Workflow",
      "summary": "Safe write and review boundary for POE Studio edits.",
      "file": "docs/agent/knowledge/workflows/overlay-staging.md",
      "keywords": ["overlay", "staging", "write", "review", "patch build"],
      "appliesWhen": ["user asks to modify resources or review changes"],
      "priority": 70
    },
    {
      "sectionId": "workflow.resource-indexing",
      "title": "Resource Indexing And Raw Reads",
      "summary": "Profile, virtual resource paths, raw reads, Native/GGPK/Oodle boundaries.",
      "file": "docs/agent/knowledge/workflows/resource-indexing.md",
      "keywords": ["profile", "resource", "index", "Native", "GGPK", "Oodle"],
      "appliesWhen": ["user asks to search/read resources or diagnose missing resources"],
      "priority": 60
    },
    {
      "sectionId": "diagnostics.tool-fit-and-capability-gap",
      "title": "Tool Fit And Capability Gap Diagnostics",
      "summary": "How Codex should detect wrong-tool success and missing capabilities.",
      "file": "docs/agent/knowledge/diagnostics/tool-fit-and-capability-gap.md",
      "keywords": ["tool mismatch", "capability gap", "zero candidates", "user correction"],
      "appliesWhen": ["tool result conflicts with user goal or user correction"],
      "priority": 85
    },
    {
      "sectionId": "diagnostics.learning-loop-v0.2",
      "title": "Learning Loop v0.2 Boundary",
      "summary": "Follow-up boundary for persistent learning events and acceptance replay.",
      "file": "docs/agent/knowledge/diagnostics/learning-loop-v0.2.md",
      "keywords": ["learning event", "user correction", "acceptance replay", "self-improvement"],
      "appliesWhen": ["planning follow-up self-learning work"],
      "priority": 40
    }
  ]
}
```

- [x] **步骤 6：更新现有知识底座入口说明**

在 `docs/agent/poe-studio-agent-context.md` 顶部说明：

```markdown
> Runtime knowledge source: `docs/agent/knowledge/index.json`.
> This document remains background material; the Agent runtime must use the Knowledge Contract sections for prompt/MCP context.
```

在 `docs/agent/poe-studio-project-workflows.md` 顶部说明同样边界。

- [x] **步骤 7：验证文档结构**

运行：

```powershell
Test-Path docs/agent/knowledge/index.json
Get-Content docs/agent/knowledge/index.json | ConvertFrom-Json | Select-Object version, coreSectionId
rg -n "source.*reference|target.*editable|current-view|capability gap" docs/agent/knowledge
rg -n "must not route|fixed tool mapping|learning event|acceptance replay" docs/agent/knowledge
```

预期：

- `version` 为 `0.1`。
- `coreSectionId` 为 `core.contract`。
- rg 能找到 source/reference、target/editable、current-view、capability gap 规则。
- rg 能找到禁止固定工具映射、learning event 和 acceptance replay 边界。

- [ ] **步骤 8：提交任务 1**

运行：

```powershell
git add docs/agent/knowledge docs/agent/poe-studio-agent-context.md docs/agent/poe-studio-project-workflows.md
git commit -m "docs(agent): add knowledge contract v0.1 skeleton (task 1)"
```

提交前必须运行：

```text
gitnexus_detect_changes(scope="staged")
```

---

## 任务 2：实现 AgentKnowledgeStore

**文件：**

- 创建：`src/PoeStudio.Contracts/AgentKnowledgeDtos.cs`
- 创建：`src/PoeStudio.Storage/Agent/AgentKnowledgeStore.cs`
- 测试：`tests/PoeStudio.Tests/AgentKnowledgeStoreTests.cs`

- [x] **步骤 1：GitNexus 影响分析**

运行：

```text
gitnexus_impact(target="AgentKnowledgeStore", direction="upstream")
gitnexus_impact(target="AgentKnowledgeDtos", direction="upstream")
```

如果目标不存在，记录“新增类型，无 upstream 依赖”，继续。

- [x] **步骤 2：编写失败测试：读取索引和 section**

创建 `tests/PoeStudio.Tests/AgentKnowledgeStoreTests.cs`：

```csharp
using PoeStudio.Storage.Agent;

namespace PoeStudio.Tests;

public sealed class AgentKnowledgeStoreTests
{
    [Fact]
    public async Task ReadSectionsAsync_returns_registered_sections_only()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var store = new AgentKnowledgeStore(root);

        var result = await store.ReadSectionsAsync(["core.contract"], 12000, CancellationToken.None);

        Assert.Equal("0.1", result.Version);
        var section = Assert.Single(result.Sections);
        Assert.Equal("core.contract", section.SectionId);
        Assert.Contains("source", section.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.MissingSectionIds);
    }

    [Fact]
    public async Task ReadSectionsAsync_reports_missing_sections()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var store = new AgentKnowledgeStore(root);

        var result = await store.ReadSectionsAsync(["missing.section"], 12000, CancellationToken.None);

        Assert.Empty(result.Sections);
        Assert.Contains("missing.section", result.MissingSectionIds);
    }

    [Fact]
    public async Task ReadSectionsAsync_limits_total_bytes()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var store = new AgentKnowledgeStore(root);

        var result = await store.ReadSectionsAsync(["core.contract", "workflow.datc64-translation"], 1000, CancellationToken.None);

        Assert.True(result.TotalBytes <= 1000);
        Assert.Contains(result.Sections, section => section.Truncated);
    }
}
```

- [x] **步骤 3：运行测试验证失败**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentKnowledgeStoreTests"
```

预期：FAIL，类型不存在。

- [x] **步骤 4：新增 DTO**

创建 `src/PoeStudio.Contracts/AgentKnowledgeDtos.cs`：

```csharp
namespace PoeStudio.Contracts;

public sealed record AgentKnowledgeIndexDto(
    string Version,
    DateTimeOffset UpdatedAt,
    string CoreSectionId,
    IReadOnlyList<AgentKnowledgeSectionIndexDto> Sections);

public sealed record AgentKnowledgeSectionIndexDto(
    string SectionId,
    string Title,
    string Summary,
    string File,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> AppliesWhen,
    int Priority);

public sealed record AgentKnowledgeReadResultDto(
    string Version,
    IReadOnlyList<string> RequestedSectionIds,
    IReadOnlyList<AgentKnowledgeSectionDto> Sections,
    IReadOnlyList<string> MissingSectionIds,
    int TotalBytes);

public sealed record AgentKnowledgeSectionDto(
    string SectionId,
    string Title,
    string Summary,
    string SourceFile,
    string Content,
    bool Truncated);
```

- [x] **步骤 5：实现 AgentKnowledgeStore**

创建 `src/PoeStudio.Storage/Agent/AgentKnowledgeStore.cs`：

```csharp
using System.Text;
using System.Text.Json;
using PoeStudio.Contracts;

namespace PoeStudio.Storage.Agent;

public sealed class AgentKnowledgeStore
{
    private readonly string repoRoot;
    private readonly string indexPath;

    public AgentKnowledgeStore(string repoRoot)
    {
        this.repoRoot = Path.GetFullPath(repoRoot);
        indexPath = Path.Combine(this.repoRoot, "docs", "agent", "knowledge", "index.json");
    }

    public async Task<AgentKnowledgeIndexDto> ReadIndexAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(indexPath);
        var index = await JsonSerializer.DeserializeAsync<AgentKnowledgeIndexDto>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken);
        return index ?? throw new InvalidOperationException("Agent knowledge index is empty.");
    }

    public async Task<AgentKnowledgeReadResultDto> ReadSectionsAsync(
        IReadOnlyList<string> sectionIds,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        maxBytes = Math.Clamp(maxBytes, 1000, 24000);
        var index = await ReadIndexAsync(cancellationToken);
        var requested = sectionIds.Take(5).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        var byId = index.Sections.ToDictionary(section => section.SectionId, StringComparer.Ordinal);
        var sections = new List<AgentKnowledgeSectionDto>();
        var missing = new List<string>();
        var totalBytes = 0;

        foreach (var sectionId in requested)
        {
            if (!byId.TryGetValue(sectionId, out var entry))
            {
                missing.Add(sectionId);
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(repoRoot, entry.File));
            if (!IsSameOrChildPath(repoRoot, fullPath))
            {
                missing.Add(sectionId);
                continue;
            }

            var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var bytes = Encoding.UTF8.GetByteCount(content);
            var remaining = maxBytes - totalBytes;
            if (remaining <= 0)
            {
                sections.Add(ToSection(entry, string.Empty, truncated: true));
                continue;
            }

            var truncated = bytes > remaining;
            if (truncated)
            {
                content = TrimToUtf8Bytes(content, remaining);
                bytes = Encoding.UTF8.GetByteCount(content);
            }

            totalBytes += bytes;
            sections.Add(ToSection(entry, content, truncated));
        }

        return new AgentKnowledgeReadResultDto(index.Version, requested, sections, missing, totalBytes);
    }

    private static AgentKnowledgeSectionDto ToSection(AgentKnowledgeSectionIndexDto entry, string content, bool truncated)
        => new(entry.SectionId, entry.Title, entry.Summary, entry.File, content, truncated);

    private static string TrimToUtf8Bytes(string value, int maxBytes)
    {
        if (maxBytes <= 0) return string.Empty;
        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var ch in value)
        {
            var charBytes = Encoding.UTF8.GetByteCount(new[] { ch });
            if (bytes + charBytes > maxBytes) break;
            builder.Append(ch);
            bytes += charBytes;
        }

        return builder.ToString();
    }

    private static bool IsSameOrChildPath(string rootFullPath, string candidateFullPath)
    {
        var relative = Path.GetRelativePath(rootFullPath, candidateFullPath);
        return relative == "."
            || (!relative.StartsWith("..", StringComparison.Ordinal)
                && !Path.IsPathRooted(relative));
    }
}
```

- [x] **步骤 6：运行测试验证通过**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentKnowledgeStoreTests"
```

预期：3/3 passed。

- [ ] **步骤 7：提交任务 2**

运行：

```powershell
git add src/PoeStudio.Contracts/AgentKnowledgeDtos.cs src/PoeStudio.Storage/Agent/AgentKnowledgeStore.cs tests/PoeStudio.Tests/AgentKnowledgeStoreTests.cs
git commit -m "feat(agent): add knowledge contract store (task 2)"
```

提交前必须运行：

```text
gitnexus_detect_changes(scope="staged")
```

---

## 任务 3：新增 MCP 知识检索工具

**文件：**

- 修改：`src/PoeStudio.Mcp/PoeMcpTools.cs`
- 测试：`tests/PoeStudio.Tests/McpToolRegistryTests.cs`
- 测试：`tests/PoeStudio.Tests/McpPoeToolsTests.cs`

- [x] **步骤 1：GitNexus 影响分析**

运行：

```text
gitnexus_impact(target="PoeMcpTools", direction="upstream")
gitnexus_impact(target="RegisterAll", direction="upstream")
gitnexus_impact(target="GetProjectOverview", direction="upstream")
```

如果风险 HIGH/CRITICAL，先报告用户并等待确认。

- [x] **步骤 2：编写失败测试：工具注册**

修改 `tests/PoeStudio.Tests/McpToolRegistryTests.cs`：

```csharp
private static readonly string[] ReadOnlyToolNames =
[
    "poe_get_project_overview",
    "poe_get_project_knowledge",
    "poe_get_workspace",
    "poe_list_profiles",
    "poe_get_profile",
    "poe_get_index_status",
    "poe_search_resources",
    "poe_read_resource",
    "poe_datc64_extract_translatable_cells",
    "poe_get_current_view_context",
    "poe_find_current_table_untranslated_cells",
    "poe_find_current_table_non_simplified_chinese_cells",
    "poe_get_agent_run_trace",
    "poe_get_agent_recent_logs"
];
```

在 `Tools_list_includes_current_view_tools_as_read_only` 或新测试中断言：

```csharp
var knowledge = Assert.Single(tools, tool => tool.Name == "poe_get_project_knowledge");
Assert.Contains("project knowledge", knowledge.Description, StringComparison.OrdinalIgnoreCase);
Assert.True(knowledge.Annotations?.ReadOnlyHint);
```

- [x] **步骤 3：编写失败测试：知识工具读取 section**

在 `tests/PoeStudio.Tests/McpPoeToolsTests.cs` 添加：

```csharp
[Fact]
public async Task Get_project_knowledge_returns_requested_sections()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

    var result = await registry.CallToolAsync(
        "poe_get_project_knowledge",
        JsonSerializer.SerializeToElement(new { sectionIds = new[] { "core.contract" }, maxBytes = 12000 }),
        CancellationToken.None);
    using var payload = ParsePayload(result);

    Assert.False(result.IsError);
    Assert.Equal("0.1", payload.RootElement.GetProperty("version").GetString());
    Assert.Contains("source", payload.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
}
```

该测试直接使用 repo root，因为知识契约文件属于仓库内容，不属于 workspace 用户数据。

- [x] **步骤 4：编写失败测试：overview 返回知识目录**

修改 `Get_project_overview_prefers_current_view_tools_for_current_table_untranslated_checks` 或新增测试：

```csharp
Assert.Contains("knowledgeIndex", text);
Assert.Contains("core.contract", text);
Assert.Contains("workflow.current-view", text);
Assert.Contains("poe_get_project_knowledge", text);
Assert.DoesNotContain("This file is the always-on POE Studio Agent contract", text);
```

预期：overview 返回目录和摘要，不返回 core-contract 全文。

- [x] **步骤 5：运行测试验证失败**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~McpToolRegistryTests|FullyQualifiedName~McpPoeToolsTests.Get_project_knowledge|FullyQualifiedName~McpPoeToolsTests.Get_project_overview"
```

预期：FAIL，新工具不存在或 overview 未返回知识目录。

- [x] **步骤 6：实现 MCP 工具注册**

在 `PoeMcpTools.RegisterAll` 中，在 `poe_get_project_overview` 后注册：

```csharp
registry.Register(
    new McpToolDefinition(
        "poe_get_project_knowledge",
        "Read selected POE Studio Agent project knowledge sections by sectionId. Use this after poe_get_project_overview when a task needs workflow-specific project semantics. Does not read game resources.",
        ObjectSchema(("sectionIds", "array"), ("maxBytes", "integer")),
        ReadOnlyAnnotations),
    (arguments, cancellationToken) => GetProjectKnowledgeAsync(workspace, arguments, cancellationToken));
```

- [x] **步骤 7：实现 GetProjectKnowledgeAsync**

在 `PoeMcpTools.cs` 中添加：

```csharp
private static async Task<McpToolResult> GetProjectKnowledgeAsync(
    PoeWorkspaceResolution workspace,
    JsonElement arguments,
    CancellationToken cancellationToken)
{
    if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
    {
        return McpToolResult.Error(error);
    }

    var sectionIds = GetStringArray(arguments, "sectionIds");
    if (sectionIds.Count == 0)
    {
        return McpToolResult.Error("Argument 'sectionIds' is required.");
    }

    var maxBytes = GetInt32(arguments, "maxBytes") ?? 12000;
    var result = await new AgentKnowledgeStore(workspaceRoot).ReadSectionsAsync(sectionIds, maxBytes, cancellationToken);
    return JsonSuccess(result);
}
```

如果现有 `GetStringArray` 只支持顶层 string array，需要确认它可读取 JSON array；否则在本任务补测试和实现。

- [x] **步骤 8：收缩 GetProjectOverview**

`GetProjectOverview` 返回新增：

```csharp
knowledgeRuntime = new
{
    coreContract = "Always use the short core contract; read workflow details through poe_get_project_knowledge.",
    tool = "poe_get_project_knowledge",
    knowledgeIndex = index.Sections.Select(section => new
    {
        section.SectionId,
        section.Title,
        section.Summary,
        section.Keywords,
        section.AppliesWhen,
        section.Priority
    })
}
```

保留已有 current-view / raw fallback guidance，但避免复制长篇 contract 全文。

- [x] **步骤 9：运行测试验证通过**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~McpToolRegistryTests|FullyQualifiedName~McpPoeToolsTests"
```

预期：全部通过。

- [ ] **步骤 10：提交任务 3**

运行：

```powershell
git add src/PoeStudio.Mcp/PoeMcpTools.cs tests/PoeStudio.Tests/McpToolRegistryTests.cs tests/PoeStudio.Tests/McpPoeToolsTests.cs
git commit -m "feat(agent): expose project knowledge MCP tool (task 3)"
```

提交前必须运行：

```text
gitnexus_detect_changes(scope="staged")
```

---

## 任务 4：改造 ChatService Prompt 为核心契约 + 知识路由

**文件：**

- 修改：`src/PoeStudio.Api/ChatService.cs`
- 测试：`tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs`

- [x] **步骤 1：GitNexus 影响分析**

运行：

```text
gitnexus_impact(target="ChatService", direction="upstream")
gitnexus_impact(target="BuildPrompt", direction="upstream")
```

如果风险 HIGH/CRITICAL，先报告用户并等待确认。

- [x] **步骤 2：编写失败测试：prompt 包含 task frame 和知识工具**

在 `ChatServiceIntegrationTests.cs` 新增：

```csharp
[Fact]
public async Task Prompt_uses_knowledge_contract_and_task_frame_without_full_knowledge_dump()
{
    string? capturedPrompt = null;
    var runner = new FakeCodexRunner((settings, prompt, onEvent, ct) =>
    {
        capturedPrompt = prompt;
        return Task.FromResult(new CodexRunResult(0, false, false, [], null));
    });
    var root = Path.Combine(Path.GetTempPath(), "poe-chat-knowledge-" + Guid.NewGuid().ToString("N"));
    var service = CreateChatService(runner, CreateWorkspaceRoot(root));

    await foreach (var _ in service.RunCodexAsync(
        "检查当前表格中还没有翻译成简中内容的繁中单元格",
        "target",
        "data/balance/traditional chinese/activeskills.datc64",
        "source",
        "target",
        "data/balance/simplified chinese/activeskills.datc64",
        "data/balance/traditional chinese/activeskills.datc64",
        CreateTableComparisonCurrentView(),
        CancellationToken.None))
    {
    }

    Assert.NotNull(capturedPrompt);
    Assert.Contains("poe_get_project_knowledge", capturedPrompt);
    Assert.Contains("Task Frame", capturedPrompt);
    Assert.Contains("toolFitCheck", capturedPrompt);
    Assert.Contains("source/current source means reference", capturedPrompt);
    Assert.Contains("target/current target means editable", capturedPrompt);
    Assert.DoesNotContain("This file is the always-on POE Studio Agent contract", capturedPrompt);
}
```

如果没有 `CreateTableComparisonCurrentView()` helper，按现有 current-view 测试内联创建。

- [x] **步骤 3：运行测试验证失败**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~ChatServiceIntegrationTests.Prompt_uses_knowledge_contract_and_task_frame_without_full_knowledge_dump"
```

预期：FAIL，prompt 还未包含新协议。

- [x] **步骤 4：修改 BuildPrompt**

在 `BuildPrompt` 中保留简短 session context，然后加入：

```csharp
lines.Add("Agent Knowledge Contract:");
lines.Add("- source/current source means reference table or reference resource.");
lines.Add("- target/current target means editable target and overlay write target.");
lines.Add("- Do not infer desired output language from profile names or resource paths.");
lines.Add("- Current table/draft/comparison tasks must inspect current-view first when currentViewContextId exists.");
lines.Add("- Use poe_get_project_knowledge to read workflow-specific knowledge sections by sectionId.");
lines.Add("");
lines.Add("Task Frame: Before choosing tools, internally identify userGoal, currentState, reference, editableTarget, desiredOutputLanguage, writeIntent, preferredContext, requiredKnowledge, and toolFitCheck.");
lines.Add("Tool Fit: A successful tool result can still be the wrong tool. If the tool semantics do not answer the user's task, choose a better tool or report capability_gap.");
```

current-view 存在时，添加推荐 knowledge sections：

```csharp
lines.Add("Recommended knowledge sections for current table tasks: core.contract, workflow.current-view, workflow.datc64-translation, diagnostics.tool-fit-and-capability-gap.");
```

删除或避免继续堆单场景长规则。

- [x] **步骤 5：运行 ChatService 测试**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~ChatServiceIntegrationTests"
```

预期：全部通过。

- [ ] **步骤 6：提交任务 4**

运行：

```powershell
git add src/PoeStudio.Api/ChatService.cs tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs
git commit -m "feat(agent): route prompts through knowledge contract (task 4)"
```

提交前必须运行：

```text
gitnexus_detect_changes(scope="staged")
```

---

## 任务 5：前端工具摘要和 run 可读性

**文件：**

- 修改：`src/PoeStudio.Api/wwwroot/app.js`
- 测试：`tests/PoeStudio.Tests/FrontendDatc64WorkflowTests.cs`

- [x] **步骤 1：GitNexus 影响分析**

运行：

```text
gitnexus_impact(target="addChatToolCall", direction="upstream")
gitnexus_impact(target="summarizeChatToolResult", direction="upstream")
```

如果 `summarizeChatToolResult` 尚未被 GitNexus 索引，记录为前端新增函数，继续。

- [x] **步骤 2：编写失败测试**

在 `FrontendDatc64WorkflowTests.cs` 的工具摘要测试中加入断言：

```csharp
Assert.Contains("poe_get_project_knowledge", appJs);
Assert.Contains("知识块", appJs);
Assert.DoesNotContain("section.Content", appJs);
```

- [x] **步骤 3：运行测试验证失败**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~FrontendDatc64WorkflowTests.Chat_tool_call_renderer_summarizes_large_tool_results"
```

预期：FAIL，前端未摘要新工具。

- [x] **步骤 4：实现摘要**

在 `summarizeChatToolResult` 增加：

```javascript
if (tool === "poe_get_project_knowledge") {
  const sections = Array.isArray(data.sections) ? data.sections : [];
  const first = sections[0];
  return [
    `知识块：${sections.length}`,
    `缺失：${data.missingSectionIds?.length ?? 0}`,
    first ? `示例：${first.sectionId} / ${first.title}` : "未返回知识块"
  ].join("\n");
}
```

不得把 section content 全文渲染到工具卡片。

- [x] **步骤 5：运行前端静态测试**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~FrontendDatc64WorkflowTests"
```

预期：全部通过。

- [ ] **步骤 6：提交任务 5**

运行：

```powershell
git add src/PoeStudio.Api/wwwroot/app.js tests/PoeStudio.Tests/FrontendDatc64WorkflowTests.cs
git commit -m "feat(agent): summarize project knowledge tool results (task 5)"
```

提交前必须运行：

```text
gitnexus_detect_changes(scope="staged")
```

---

## 任务 6：记录 task frame、tool-fit 和 capability-gap 事件

**文件：**

- 创建：`src/PoeStudio.Contracts/AgentTaskFrameDtos.cs`
- 修改：`src/PoeStudio.Api/ChatService.cs`
- 测试：`tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs`
- 测试：`tests/PoeStudio.Tests/AgentTaskFrameTraceTests.cs`

- [x] **步骤 1：GitNexus 影响分析**

运行：

```text
gitnexus_impact(target="ChatService", direction="upstream")
gitnexus_impact(target="AppendCodexEventAsync", direction="upstream")
```

如果风险 HIGH/CRITICAL，先报告用户并等待确认。

- [x] **步骤 2：编写失败测试：task frame 进入 trace**

创建 `tests/PoeStudio.Tests/AgentTaskFrameTraceTests.cs`：

```csharp
using Microsoft.Extensions.Configuration;
using PoeStudio.Api;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;
using PoeStudio.Storage.Agent;

namespace PoeStudio.Tests;

public sealed class AgentTaskFrameTraceTests
{
    [Fact]
    public async Task ChatService_records_task_frame_events_in_run_trace()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-task-frame-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var traceStore = new AgentRunTraceStore(root);
        var events = new List<CodexParsedEvent>
        {
            new(
                "{}",
                CodexParsedEventType.AgentMessage,
                """{"type":"agent_task_frame","taskFrame":{"userGoal":"check target cells","currentState":"tableComparison","reference":"current source table","editableTarget":"current target table","desiredOutputLanguage":"Simplified Chinese","writeIntent":"read-only","preferredContext":"current-view","requiredKnowledge":["core.contract"],"toolFitCheck":"Need non-simplified detector."}}""",
                null,
                false,
                false,
                null)
        };
        var service = CreateChatService(events, traceStore, root);

        await foreach (var _ in service.RunCodexAsync("检查当前表格繁中", null, null, null, null, null, null, null, CancellationToken.None))
        {
        }

        var runId = Directory.EnumerateFiles(Path.Combine(root, "agent", "runs"), "*.jsonl")
            .Select(Path.GetFileNameWithoutExtension)
            .Single();
        var trace = await traceStore.ReadAsync(runId!, CancellationToken.None);

        Assert.Contains(trace, evt => evt.EventName == "task_frame" && evt.DataJson.Contains("toolFitCheck", StringComparison.Ordinal));
    }
}
```

- [x] **步骤 3：编写失败测试：capability gap 进入 trace**

在同一测试文件中添加：

```csharp
[Fact]
public async Task ChatService_records_capability_gap_events_in_run_trace()
{
    var root = Path.Combine(Path.GetTempPath(), "poe-capability-gap-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var traceStore = new AgentRunTraceStore(root);
    var events = new List<CodexParsedEvent>
    {
        new(
            "{}",
            CodexParsedEventType.AgentMessage,
            """{"type":"agent_capability_gap","failureType":"tool_semantics_mismatch","userGoal":"check Traditional Chinese target cells","missingCapability":"non-simplified current target detector","proposedNextAction":"use poe_find_current_table_non_simplified_chinese_cells"}""",
            null,
            false,
            false,
            null)
    };
    var service = CreateChatService(events, traceStore, root);

    await foreach (var _ in service.RunCodexAsync("为什么你刚才说没有漏翻", null, null, null, null, null, null, null, CancellationToken.None))
    {
    }

    var runId = Directory.EnumerateFiles(Path.Combine(root, "agent", "runs"), "*.jsonl")
        .Select(Path.GetFileNameWithoutExtension)
        .Single();
    var trace = await traceStore.ReadAsync(runId!, CancellationToken.None);

    Assert.Contains(trace, evt => evt.EventName == "capability_gap" && evt.DataJson.Contains("tool_semantics_mismatch", StringComparison.Ordinal));
}

private static ChatService CreateChatService(
    IReadOnlyList<CodexParsedEvent> events,
    AgentRunTraceStore traceStore,
    string root)
{
    var runner = new FakeCodexRunner(async (settings, prompt, onEvent, ct) =>
    {
        foreach (var evt in events)
        {
            if (onEvent is not null)
            {
                await onEvent(evt);
            }
        }

        return new CodexRunResult(0, false, false, events, null);
    });
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PoeStudio:WorkspaceRoot"] = root
        })
        .Build();
    var workspaceRoot = new WorkspaceRootProvider(config);
    return new ChatService(
        runner,
        workspaceRoot,
        config,
        new AgentCurrentViewStore(workspaceRoot.CurrentRoot),
        traceStore,
        TimeSpan.FromSeconds(30));
}

private sealed class FakeCodexRunner : ICodexProcessRunner
{
    private readonly Func<AgentSettingsDto, string, Func<CodexParsedEvent, Task>?, CancellationToken, Task<CodexRunResult>> handler;

    public FakeCodexRunner(Func<AgentSettingsDto, string, Func<CodexParsedEvent, Task>?, CancellationToken, Task<CodexRunResult>> handler)
    {
        this.handler = handler;
    }

    public Task<CodexRunResult> RunAsync(AgentSettingsDto settings, string prompt, Func<CodexParsedEvent, Task>? onEvent, CancellationToken cancellationToken)
        => handler(settings, prompt, onEvent, cancellationToken);
}
```

- [x] **步骤 4：运行测试验证失败**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentTaskFrameTraceTests"
```

预期：FAIL，当前 ChatService 只把这类 JSON 当普通 assistant message。

- [x] **步骤 5：新增 DTO**

创建 `src/PoeStudio.Contracts/AgentTaskFrameDtos.cs`：

```csharp
namespace PoeStudio.Contracts;

public sealed record AgentTaskFrameDto(
    string? UserGoal,
    string? CurrentState,
    string? Reference,
    string? EditableTarget,
    string? DesiredOutputLanguage,
    string? WriteIntent,
    string? PreferredContext,
    IReadOnlyList<string>? RequiredKnowledge,
    string? ToolFitCheck);

public sealed record AgentCapabilityGapDto(
    string? FailureType,
    string? UserGoal,
    string? MissingCapability,
    string? ProposedNextAction);
```

- [x] **步骤 6：实现 trace event 识别**

在 `ChatService.AppendCodexEventAsync` 附近新增 helper：

```csharp
private async Task AppendSemanticAgentEventsAsync(string runId, CodexParsedEvent parsedEvent, CancellationToken cancellationToken)
{
    if (parsedEvent.EventType != CodexParsedEventType.AgentMessage || string.IsNullOrWhiteSpace(parsedEvent.Message))
    {
        return;
    }

    using var document = TryParseJson(parsedEvent.Message);
    if (document is null || !document.RootElement.TryGetProperty("type", out var typeElement))
    {
        return;
    }

    var type = typeElement.GetString();
    if (type == "agent_task_frame" && document.RootElement.TryGetProperty("taskFrame", out var taskFrame))
    {
        await _traceStore.AppendAsync(runId, new AgentRunTraceEventDto("task_frame", "observed", taskFrame.GetRawText(), DateTimeOffset.UtcNow), cancellationToken);
    }
    else if (type == "agent_capability_gap")
    {
        await _traceStore.AppendAsync(runId, new AgentRunTraceEventDto("capability_gap", "observed", document.RootElement.GetRawText(), DateTimeOffset.UtcNow), cancellationToken);
    }
}
```

在 `AppendCodexEventAsync` 写普通事件后调用该 helper。

实现 `TryParseJson` 时必须吞掉 JSON parse error，不得影响普通 assistant message。

建议实现：

```csharp
private static JsonDocument? TryParseJson(string value)
{
    try
    {
        return JsonDocument.Parse(value);
    }
    catch (JsonException)
    {
        return null;
    }
}
```

- [x] **步骤 7：运行测试验证通过**

运行：

```powershell
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentTaskFrameTraceTests|FullyQualifiedName~ChatServiceIntegrationTests"
```

预期：全部通过。

- [ ] **步骤 8：提交任务 6**

运行：

```powershell
git add src/PoeStudio.Contracts/AgentTaskFrameDtos.cs src/PoeStudio.Api/ChatService.cs tests/PoeStudio.Tests/AgentTaskFrameTraceTests.cs tests/PoeStudio.Tests/ChatServiceIntegrationTests.cs
git commit -m "feat(agent): trace task frame and capability gaps (task 6)"
```

提交前必须运行：

```text
gitnexus_detect_changes(scope="staged")
```

---

## 任务 7：实机验收报告和回归证据

**文件：**

- 创建：`docs/superpowers/reports/2026-05-25-agent-knowledge-contract-v0.1-acceptance.md`
- 可选修改：`docs/ai-project-memory.md` 的自定义区域，若已有约定允许；否则只写报告。

- [ ] **步骤 1：运行完整验证**

运行：

```powershell
dotnet build PoeStudio.sln --no-restore
dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentKnowledgeStoreTests|FullyQualifiedName~AgentTaskFrameTraceTests|FullyQualifiedName~McpToolRegistryTests|FullyQualifiedName~McpPoeToolsTests|FullyQualifiedName~ChatServiceIntegrationTests|FullyQualifiedName~FrontendDatc64WorkflowTests"
dotnet test PoeStudio.sln --no-restore --no-build
git diff --check
```

预期：

- build 0 errors。
- targeted tests 0 failures。
- full tests 0 failures。
- diff check 0 whitespace errors。

- [ ] **步骤 2：GitNexus 变更检测**

运行：

```text
gitnexus_detect_changes(scope="all")
```

预期：

- 影响范围集中在 Agent knowledge、MCP tools、ChatService prompt、frontend chat renderer、测试。
- 若 HIGH/CRITICAL，报告具体受影响流程和原因。

- [ ] **步骤 3：重启项目**

运行：

```powershell
Get-NetTCPConnection -LocalPort 5010 -State Listen -ErrorAction SilentlyContinue |
  Select-Object -ExpandProperty OwningProcess -Unique |
  ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }

Get-Process PoeStudio.Api,PoeStudio.Mcp -ErrorAction SilentlyContinue |
  Stop-Process -Force -ErrorAction SilentlyContinue

Start-Process -FilePath dotnet `
  -ArgumentList @('run','--project','src/PoeStudio.Api/PoeStudio.Api.csproj','--urls','http://localhost:5010') `
  -WorkingDirectory (Get-Location) `
  -RedirectStandardOutput 'poe-studio-dev.out.log' `
  -RedirectStandardError 'poe-studio-dev.err.log' `
  -WindowStyle Hidden

Start-Sleep -Seconds 8
Invoke-WebRequest -Uri 'http://localhost:5010/api/health' -UseBasicParsing -TimeoutSec 10
```

预期：HTTP 200，body 包含 `"status":"ok"`。

- [ ] **步骤 4：执行实机场景 A**

在 UI 中打开 activeskills 当前对比表后，输入：

```text
检查当前表格中还没有翻译成简中内容的繁中单元格
```

记录：

- Codex 是否调用 `poe_get_current_view_context`。
- Codex 是否调用 `poe_get_project_knowledge`。
- Codex 是否调用 `poe_find_current_table_non_simplified_chinese_cells`。
- run trace 是否包含 `task_frame`。
- 回答是否包含第 16 行 `Description @16` 或其他繁中候选。
- 是否错误要求切换 source/target。
- 是否错误写入 overlay。

预期：

- 必须调用 current-view。
- 必须按需读取 knowledge。
- 必须使用非简中检测工具或明确说明工具缺口。
- 必须记录 task frame 或 capability gap。
- 不写 overlay。

- [ ] **步骤 5：执行实机场景 B**

输入：

```text
为什么你刚才说没有漏翻，但我看到目标表里还有繁中？
```

预期：

- 归因 `tool_semantics_mismatch`。
- 说明旧工具不是繁中检测工具。
- 不重复用旧工具 0 候选压用户。
- run trace 包含 `capability_gap` 或等价诊断事件。

- [ ] **步骤 6：执行实机场景 C**

输入：

```text
把这些繁中单元格改成简中
```

预期：

- 先给 proposal。
- 明确写 target overlay staging。
- 不改 source。
- 若写 DATC64 binary 工具不足，报告 capability gap 并请求批准补工具。

- [ ] **步骤 7：执行实机场景 D**

输入：

```text
帮我检查 patch build 为什么失败
```

预期：

- 读取 patch/overlay/resource knowledge。
- 不读取 DATC64 translation knowledge，除非错误直接关联 DATC64 overlay。

- [ ] **步骤 8：编写验收报告**

创建 `docs/superpowers/reports/2026-05-25-agent-knowledge-contract-v0.1-acceptance.md`：

```markdown
# POE Studio Agent Knowledge Contract v0.1 Acceptance

## Build And Tests

- `dotnet build PoeStudio.sln --no-restore`: record exit code, warnings, errors.
- targeted tests: record command, total passed, failed, skipped.
- full tests: record command, total passed, failed, skipped.
- `git diff --check`: record exit code and whitespace findings.
- GitNexus detect changes: record risk level, changed symbols summary, affected processes.
- semantic trace events: record whether `task_frame` and `capability_gap` appeared in live runs.

## Live Acceptance

### Scenario A: Current Table Non-Simplified Check

- Prompt: paste the exact user prompt.
- Tools observed: list tool names in order.
- Result: summarize returned rows/cells and whether row 16 `Description @16` was detected when present.
- Pass/Fail: write `PASS` or `FAIL` and one sentence of evidence.

### Scenario B: Tool Mismatch Explanation

- Prompt: paste the exact user prompt.
- Tools observed: list tool names in order.
- Result: record whether the Agent identified `tool_semantics_mismatch`.
- Pass/Fail: write `PASS` or `FAIL` and one sentence of evidence.

### Scenario C: Overlay Write Boundary

- Prompt: paste the exact user prompt.
- Tools observed: list tool names in order.
- Result: record whether the Agent proposed target overlay staging and preserved source/reference.
- Pass/Fail: write `PASS` or `FAIL` and one sentence of evidence.

### Scenario D: Knowledge On Demand

- Prompt: paste the exact user prompt.
- Tools observed: list tool names in order.
- Result: record which knowledge sections were read and whether irrelevant DATC64 sections were avoided.
- Pass/Fail: write `PASS` or `FAIL` and one sentence of evidence.

## Residual Risks

- List any failed or partial live scenarios with concrete blocker, date, and next action.
```

- [ ] **步骤 9：提交任务 7**

运行：

```powershell
git add docs/superpowers/reports/2026-05-25-agent-knowledge-contract-v0.1-acceptance.md
git commit -m "test(agent): record knowledge contract acceptance (task 7)"
```

提交前必须运行：

```text
gitnexus_detect_changes(scope="staged")
```

---

## 5. 最终验收清单

- [ ] `docs/agent/knowledge/index.json` 是运行时知识目录。
- [ ] `core-contract.md` 不超过 1000 token 目标，且只包含硬语义。
- [ ] `poe_get_project_overview` 返回知识目录，不返回整本文档。
- [ ] `poe_get_project_knowledge` 可按 section 读取知识块，并限制 maxBytes。
- [ ] `ChatService` prompt 不再堆所有场景规则，而是注入核心契约和 task-frame/tool-fit 协议。
- [ ] Codex 在当前表格繁中未转简中场景中读取 current-view 和相关 knowledge section。
- [ ] run trace 能记录 task frame 和 capability gap。
- [ ] Codex 不再把 source/target 当语言方向。
- [ ] Codex 能解释旧工具和用户目标不匹配。
- [ ] 写入仍只走 target overlay staging。
- [ ] 用户纠错可以进入 capability gap / tool mismatch 归因。
- [ ] targeted tests、full tests、build、diff check、GitNexus detect_changes 均有记录。
- [ ] 真实 UI + 真实 Codex + 真实 MCP 验收报告已提交。

---

## 6. v0.2 接续边界

v0.1 完成后仍不得宣称 Agent 已具备完整自学习。v0.1 的边界是：知识契约结构化、按需读取、task frame/tool-fit 可追踪、capability gap 可记录、实机验收可人工复核。

v0.2 必须另立计划，目标是把“用户纠错 → 系统资产”的闭环做成可查询、可回放、可批准执行的机制。

### v0.2 必须覆盖

- [ ] **Learning Event Store**
  - 存储用户纠错、Agent 归因、建议修改的 knowledge section、建议测试、建议工具变更。
  - 支持按 `failureType`、`sectionId`、`toolName`、`runId` 查询。
- [ ] **Knowledge Update Proposal**
  - Agent 不能直接改知识底座；必须生成 proposal，经用户批准后才能修改。
  - proposal 必须包含：原始纠错、归因、拟修改文件、拟新增测试、验收方式。
- [ ] **Acceptance Replay Harness**
  - 固定 current-view snapshot fixture 和工具结果 fixture。
  - 能回放“当前表格繁中未转简中”和“工具语义不匹配解释”场景。
  - 能在没有真实游戏资源的情况下验证 task frame、knowledge section、tool choice 和 final answer 结构。
- [ ] **Live Regression Gate**
  - 每次修改 Agent knowledge / MCP 工具 / ChatService prompt 后，至少运行一个 replay 场景。
  - 每次 release 前运行真实 UI + 真实 Codex smoke。
- [ ] **Coverage Expansion**
  - 把知识块扩展到 patch build、migration、Native/GGPK write、profile/index 管理。
  - 每新增一个 workflow section，必须有至少一个 replay 或 MCP behavior test。

### v0.2 不得做

- 不得恢复后端 Planner。
- 不得把 replay harness 变成固定答案脚本。
- 不得让 Agent 自动修改知识底座而无用户批准。
- 不得用“记忆文件已更新”替代测试和实机验收。

---

## 7. 执行交接

建议使用 **subagent-driven-development** 执行，每 1 个任务一个子代理，任务之间做审查；若在当前会话内执行，则使用 **executing-plans**，每完成 3 个任务暂停一次方向审查。

执行顺序不可跳过：

1. 任务 1 先建立知识文件和索引。
2. 任务 2 再实现 store。
3. 任务 3 再暴露 MCP 工具。
4. 任务 4 再改 prompt。
5. 任务 5 再改 UI 摘要。
6. 任务 6 再记录 task frame / tool-fit / capability-gap 事件。
7. 任务 7 最后做全量验证和实机验收报告。

不得先改 ChatService prompt 来“临时看起来好用”；那会回到散乱提示词模式。
