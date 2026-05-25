# POE Studio Agent 项目信息流

> 本文是 POE Studio 内置 Agent 的项目知识底座。它不是实现计划，也不替代阶段计划；它用于约束 Agent 如何理解项目、工作流、工具、路径、草稿层和审批边界。  
> 当前状态：基于 2026-05-23 对代码、阶段计划、验收报告和真实 DATC64 翻译测试问题的梳理。

## 1. Agent 总目标

POE Studio 需要接入的是全量项目助手，不是固定脚本入口。用户用自然语言提出目标后，Agent 应该能：

- 理解当前项目、profile、资源路径、索引、overlay、表格和补丁流程。
- 主动调用已有 MCP/API/项目工具读取上下文。
- 在没有现成工具时，先提出工具或脚本方案，经过审批后再执行。
- 对任何写入行为先产出计划、候选、审批点和可追踪记录。
- 把 DATC64 翻译作为首个受控能力样例，而不是把 Agent 收窄成 DATC64 专用工具。

当前架构：CODEX 薄桥接。前端 SSE chat → ChatService → CodexProcessRunner → `codex exec --json` → PoeStudio.Mcp tools → overlay staging。Stage 3 才是 IDE-like Agent Workspace UI。

### 当前工作态上下文

用户说“当前表格”“当前草稿”“当前对比视图”时，Agent 必须理解为 UI 当前工作态，而不是底层资源文件。前端会在 `/api/chat` 中提交 `currentView` 摘要，后端保存为短期 `currentViewContextId`，Codex 通过 MCP 工具读取。

可用工具：

- `poe_get_current_view_context`：读取当前 UI 工作态快照。
- `poe_find_current_table_untranslated_cells`：基于当前已打开目标表和来源参考表查找漏翻候选。

规则：

- 当前表格检查优先使用 current-view 工具。
- 不要默认调用 `poe_datc64_extract_translatable_cells`，因为该工具读取底层资源，可能触发 Native/GGPK/Oodle，并且不代表 UI 当前工作态。
- 只有用户明确要求重新读取原始资源时，才使用 raw resource 工具。

## 2. 当前已确认架构事实

POE Studio 是 .NET 8 + ASP.NET Core Minimal API 本地 Web 工具，前端在 `src/PoeStudio.Api/wwwroot`，没有独立前端构建链。

核心分层如下：

| 层 | 职责 |
| --- | --- |
| `PoeStudio.Contracts` | DTO、枚举、API 契约 |
| `PoeStudio.Core` | 纯业务逻辑，例如资源分类、预览、DATC64、Native、补丁构建、Agent prompt/runner/parser |
| `PoeStudio.Storage` | 本地持久化，例如 profile、资源索引、overlay、audit |
| `PoeStudio.Api` | HTTP 路由、依赖注入、后台任务、静态前端 |
| `PoeStudio.Mcp` | Stage 1 MCP stdio server，供 Codex 调用 POE Studio 只读工具 |

Architecture evolution:

- Phase 1：`POE Studio MCP Tools`，只读 MCP 工具，CODEX 通过 `poe-studio` MCP server 调用。
- Phase 2（当前）：CODEX 薄桥接。`POST /api/chat` SSE 端点 + `CodexProcessRunner(codex exec --json)` + MCP 工具（读写）+ overlay staging。
- Phase 3（规划中）：Agent Workspace UI，类似 Codex / IDE 插件体验。

## 3. Profile 与工作区语义

所有用户数据按 workspace 和 profile 隔离。默认 workspace 由 `WorkspaceRootProvider` 管理，可通过 `/api/workspace` 修改。

profile 目录约定由 `WorkspaceLayout.ForProfile(workspaceRoot, profileId)` 统一负责：

- `profiles/{profileId}/profile.json`：profile 元数据。
- `profiles/{profileId}/cache/raw`：原始资源缓存。
- `profiles/{profileId}/cache/preview`：预览缓存。
- `profiles/{profileId}/cache/table-schemas`：表结构缓存。
- `profiles/{profileId}/overlay/files`：草稿层实际文件。
- `profiles/{profileId}/overlay/manifest.json`：草稿层清单。
- `profiles/{profileId}/builds`：构建和安装产物。
- `profiles/{profileId}/audit`：审计记录。

当前真实翻译任务中已经出现的 profile：

| 角色 | profileId | 显示语义 |
| --- | --- | --- |
| 目标 | `75c5bef9860a45658cbb2a41aae5c057` | 国际服-目标 |
| 来源 | `50c7736ad5d64dcdbebb328a4024abd9` | 国服-简体来源 |

Agent 不能只看 profileId 字符串，需要结合用户当前选择、profile display name、资源路径和任务目标推断角色。

## 4. 资源路径与索引语义

资源索引由 `ResourceIndexStore` 管理。每条资源有：

- `VirtualPath` / `NormalizedPath`：项目内虚拟路径，必须使用 `ResourcePath.Normalize` 相关逻辑处理。
- `PhysicalPath`：可以是普通文件路径，也可以是 `native-bundles2://...` 或 `ggpk://...` 这类 Native/GGPK 定位。
- `Kind`：例如 Text、Table、Image、Ui、Binary。
- `SourceLayer`：通常是 Base，overlay 是独立草稿层，不等同于索引资源本身。

Agent 读取资源前必须先确认：

- profile 是否存在。
- 资源索引是否存在且包含目标路径。
- 物理资源是普通文件、Native Bundles2 还是 GGPK 内嵌 Bundles2。
- 需要读取的是原始层、草稿层，还是“优先草稿，缺失才回退原始层”。

## 5. 原始层、草稿层和当前工作态

这是当前 Agent 最容易跑偏的核心语义。

| 术语 | 含义 | 当前主要读取入口 |
| --- | --- | --- |
| 原始层 / Base | 索引指向的客户端原始资源 | `ReadResourceBytesAsync`、MCP `PoeResourceContentReader.ReadAsync` |
| 草稿层 / Overlay / Draft | 用户已编辑但尚未构建进补丁的替换文件 | `OverlayStore`，路径在 profile 的 `overlay/files` |
| 当前工作态 | 用户在 UI 继续编辑时看到的内容，通常是“优先草稿层，否则原始层” | `ReadResourceBytesPreferOverlayAsync`、`/api/tables/inspect` |

已确认代码事实：

- `/api/tables/inspect` 调用 `ReadResourceBytesPreferOverlayAsync`，所以 UI 打开表格时默认看到当前草稿层。
- `/api/tables/save` 也先读取 `ReadResourceBytesPreferOverlayAsync`，所以保存表格是在当前草稿基础上继续编辑。
- 前端 `state.previewUseOverlay` 默认是 `true`，界面可在草稿层/原始层之间切换。
- MCP 工具 `poe_datc64_extract_translatable_cells` 当前通过 `PoeResourceContentReader.ReadAsync` 读取索引资源，没有 overlay 参数，因此读取的是原始层。

因此，用户说“继续翻译当前表”“补全当前草稿”“只处理还没翻译的单元格”时，Agent 必须读取目标草稿层或当前工作态，不能只读取目标原始层。

## 6. DATC64 表格工作流

用户当前工作流不是单表翻译，而是跨 profile 对比翻译：

1. 选择目标 profile：国际服-目标。
2. 打开目标资源，例如 `data/balance/traditional chinese/activeskills.datc64`。
3. UI 默认优先显示目标草稿层。
4. UI 自动或手动匹配来源 profile 的参考表，例如 `data/balance/simplified chinese/activeskills.datc64`。
5. 左侧/参考侧是国服简体来源，右侧/目标侧是国际服目标。
6. 用户审查候选，确认后写入目标 profile 的 overlay draft。
7. 后续补丁构建只看目标 profile 的 overlay manifest。

前端 DATC64 对比事实：

- `inspectTable()` 调用 `/api/tables/inspect` 读取当前目标表。
- `loadTableReferenceInspection()` 根据当前 source/target profile 匹配参考资源，再调用 `/api/tables/inspect` 读取参考表。
- `renderDatc64AgGridComparison()` 负责目标/参考并排表格。
- 可编辑列来自 `editableColumnIndexes`。
- 差异判断来自参考单元格和目标单元格的值比较。

当前任务的正确默认语义：

> “翻译 `data/balance/traditional chinese/activeskills.datc64`，来源是 `data/balance/simplified chinese/activeskills.datc64`，只处理目标草稿层和简体来源不一致的单元格；两个表中内容一样，或者两边都是英文的内容，不做改动。”

Agent 应解释为：

- 目标 profile：国际服-目标。
- 来源 profile：国服-简体来源。
- 目标资源：`data/balance/traditional chinese/activeskills.datc64`。
- 来源资源：`data/balance/simplified chinese/activeskills.datc64`。
- 目标读取层：优先草稿层，缺失才读原始层。
- 来源读取层：默认原始/参考层，除非用户明确要求来源也用草稿层。
- 候选生成：只针对目标当前值与来源值不一致的可编辑翻译单元格。
- 跳过：目标与来源相同、两边都是英文、空值、数字、路径、hash、不可编辑列。
- 写入：先生成 proposal，用户批准后才写入目标 overlay draft。

## 7. 当前架构：CODEX 薄桥接

当前 CODEX 集成架构——用户输入直达 MCP 工具，无后端任务分类或审批 run：

1. 用户在聊天框输入自然语言，前端发送 `POST /api/chat` SSE 请求，携带 `{ message, profileId, resourcePath, sourceProfileId, targetProfileId, sourceResourcePath, targetResourcePath }`。其中 source/target resourcePath 由语言感知路径匹配自动推导配对资源（例如繁中 → 简中来源路径）。
2. `ChatService.RunCodexAsync` 包装 session context prompt（含 workspaceRoot、activeProfileId、selectedResourcePath），调用 `CodexProcessRunner.RunAsync`。
3. `CodexProcessRunner` 启动 `codex exec --json` 子进程，自动配置 `mcp_servers.poe-studio` 指向本地 PoeStudio.Mcp。
4. CODEX 子进程先调用 `poe_get_project_overview` 了解项目领域，然后根据用户任务调用其他 MCP 工具。
5. MCP 工具结果通过 JSONL stdout 流回，`CodexJsonEventParser` 逐行解析，通过 `Channel<T>` 桥接为 SSE events。
6. 前端 `processSseBlock` 收到 tool_call/message/error/done 事件，动态更新聊天界面和 overlay 列表。
7. `poe_write_overlay_text` / `poe_write_overlay_binary` 直接写入 overlay staging。`processSseBlock` 在写入完成后自动调用 `refreshOverlayList()`。
8. 用户通过 UI overlay 面板审查 staging 变更，手动发起 patch build 后变更才影响游戏文件。

能力概述：

| 能力 | 说明 | 工具 |
| --- | --- | --- |
| 项目认知 | CODEX 调用 `poe_get_project_overview` 了解领域术语、工作流和限制 | `poe_get_project_overview` |
| 只读查询 | 查询 workspace、profile、索引状态、搜索/读取资源 | `poe_get_workspace`、`poe_list_profiles`、`poe_get_profile`、`poe_get_index_status`、`poe_search_resources`、`poe_read_resource` |
| DATC64 分析 | 提取可翻译单元格 | `poe_datc64_extract_translatable_cells` |
| overlay 写入 | 写 text/binary 到 overlay staging | `poe_write_overlay_text`、`poe_write_overlay_binary` |
| overlay 管理 | 列出现有 overlay、还原 | `poe_list_overlays`、`poe_revert_overlay` |

关键设计决策：

- 不做后端任务分类/规划——CODEX 自主理解用户意图并选择合适的 MCP 工具。
- 没有单独的 approval run 实体——写工具直接写入 overlay staging，用户通过 UI 面板做最终审核。
- 不保留 thread/message/run 状态——每次 chat 请求独立启动 `codex exec --json`，上下文由前端维护。

### 异常路径规则

当工具调用失败、超时、无最终回答或 UI 卡住时，Agent 必须先读取 run trace 和日志摘要，自行判断断点；普通诊断不得修改代码。只有用户明确批准 repair run 后，Agent 才能修改项目代码，并且必须先写失败测试、再最小修复、再验证。

## 8. 当前已知缺口

### 8.1 Agent 与 UI 草稿层认知不一致

已确认问题：

- UI 表格读取当前草稿层。
- Agent 的 MCP DATC64 提取工具读取目标原始层。
- 因此 Agent 会把用户已经在草稿中翻译过的内容再次作为候选。

真实表现：

- 用户打开的草稿表格中某些内容已经翻译完成。
- Agent proposal 仍给出这些内容的候选。
- 原因不是“翻译规则写得不够细”，而是 Agent 读取层错误。

后续修复方向：

- MCP 读取工具需要支持 `useOverlay` 或 `preferOverlay`。
- DATC64 提取输出需要声明 `readLayer`、`fromOverlay`、`profileId`、`resourcePath`。
- 对比型任务需要能同时读取 target current/draft 与 source reference。
- prompt 需要注入本文的项目语义，尤其是“继续翻译当前表”的默认解释。

### 8.2 DATC64 proposal 缺少跨表上下文

当前 `datc64TranslationProposal` 候选只包含：

- locator
- rowIndex
- columnIndex
- sourceText
- translatedText
- confidence
- notes

缺少：

- target current text
- source reference text
- source profile/resource
- target profile/resource
- target read layer
- source read layer
- skip reason
- 是否基于 overlay

后续修复方向：proposal 需要扩展为“对比候选”，而不是单表候选。

### 8.3 Agent 项目知识没有系统注入

当前 `AgentPromptBuilder` 只写了通用执行规则、工具列表和输出 schema。它没有系统注入：

- profile 角色语义。
- 工作区和路径约定。
- base/overlay/draft 区别。
- DATC64 目标/参考表工作流。
- 当前任务默认跳过规则。
- 已知失败案例。

后续修复方向：把本文作为 Agent prompt 的项目上下文来源，或整理成可由 MCP 查询的 project context resource。

## 9. Agent 默认工具策略

Agent 处理 POE Studio 任务时，应优先按以下顺序工作：

1. 读取项目上下文：workspace、profiles、当前设置、索引状态。
2. 确认用户目标涉及哪个 profile、资源、层、写入边界。
3. 只读工具可直接调用。
4. 写入型任务先生成计划和 proposal。
5. 用户批准后，后端执行写入。
6. 写入后刷新 overlay list，并提供可审计结果。

禁止行为：

- 禁止把用户自然语言硬拆成固定脚本参数后直接执行。
- 禁止在不知道读取层的情况下生成候选。
- 禁止越过 approval 直接写 overlay。
- 禁止把“需要添加索引”作为终止回答；应该说明哪个 profile 缺索引，并给出下一步可执行操作。
- 禁止在没有项目知识上下文时假装已经理解整个工作流。

## 10. DATC64 翻译验收矩阵

后续任何 DATC64 Agent 修复，至少要通过下列验收：

| 场景 | 预期 |
| --- | --- |
| 目标无 overlay | Agent 能读取目标原始层，与来源参考表对比，生成候选 |
| 目标已有 overlay | Agent 默认读取目标草稿层，不重复生成已翻译候选 |
| 目标与来源单元格相同 | 跳过 |
| 两边都是英文 | 跳过 |
| 目标是英文、来源是中文 | 需要根据用户目标判断，不能机械覆盖 |
| 不可编辑列 | 跳过 |
| 空值、数字、路径、hash | 跳过 |
| proposal 待审批 | overlay 不变化 |
| approval 批准 | 只写目标 profile 的对应 overlay |
| approval 拒绝 | 不写 overlay，记录拒绝事件 |
| Codex/MCP 失败 | run 有 errorCode、stderr/event 证据，可重试 |

真实测试基线应至少覆盖：

- target profile：`75c5bef9860a45658cbb2a41aae5c057`
- source profile：`50c7736ad5d64dcdbebb328a4024abd9`
- target resource：`data/balance/traditional chinese/activeskills.datc64`
- source resource：`data/balance/simplified chinese/activeskills.datc64`

## 11. 后续计划应基于本文展开

正确的后续工作顺序：

1. 先把本文作为 Agent 项目上下文固定入口。
2. 再写实现计划，补齐 MCP overlay-aware read、跨 profile DATC64 对比工具、proposal schema、prompt 注入、验收测试。
3. 通过真实 profile/resource 做端到端验证。
4. 再进入更大的全量项目助手能力扩展。

不应直接做的事：

- 不应继续只改提示词来修补读取层错误。
- 不应继续让用户每次输入一长串固定规则。
- 不应把“只翻译 activeskills”做成新的专用按钮。
- 不应在没有跨表、跨层、审批和测试闭环前声明 Agent 翻译能力完成。

## 12. 当前未知项

以下内容需要后续继续验证，不能在计划中当成已确认事实：

- UI 当前选中 profile/resource 是否已经有稳定 API 暴露给 Agent，而不是只存在前端 state。
- source profile 的默认层是否永远应为原始层；目前只对当前翻译工作可这样默认。
- DATC64 所有表的可编辑列规则是否足以覆盖后续翻译任务。
- 是否需要单独的“项目知识索引”或 MCP resource，让 Agent 每次 run 前按需读取，而不是把整篇文档塞进 prompt。
- Stage 3 UI 如何展示跨 profile、跨层、候选跳过原因和审批 diff。

## 13. 本文自检

- 已把当前问题定位为读取层和项目语义缺口，没有把责任简单归因于 prompt 或用户提示不够细。
- 已区分已确认代码事实、当前真实任务语义、后续修复方向和未知项。
- 已明确 DATC64 只是受控样例，不能替代全量项目助手目标。
- 已明确后续计划必须先补 Agent 项目上下文、overlay-aware read、跨 profile 对比和 proposal schema，再做端到端验收。
- 未写入功能代码，未修改 Agent runtime、MCP 工具、API 或前端实现。
