# POE Studio 项目工作流与 Agent 知识底座梳理计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务执行此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。本文是资料梳理与文档产出计划，不是功能实现计划。

**目标：** 系统梳理 POE Studio 全项目，产出完整、可信、可被后续 Agent 自动使用的「项目工作流与 Agent 知识底座文档」。

**架构：** 本任务只做只读调查与文档沉淀。执行者需要从现有项目记忆、阶段计划、验收报告、GitNexus 图谱、代码和测试中提取事实，按用户真实工作流组织知识，最终写入 `docs/agent/poe-studio-project-workflows.md`。DATC64 只能作为一个工作流样例，不能成为文档中心。

**技术栈：** Markdown、GitNexus MCP、PowerShell、`rg`、.NET 项目源码、xUnit 测试、POE Studio 现有文档。

---

## 0. 硬约束

- [ ] **H0.1：禁止功能代码修改**  
  本计划只允许创建或修改文档。禁止修改 `src/**`、`tests/**`、前端文件、项目文件、配置文件或运行会改变项目状态的脚本。

- [ ] **H0.2：禁止写 Agent 修复计划**  
  本计划的交付物不是 Stage 2/Stage 3 实现计划，不允许把内容写成「如何修 DATC64」「如何改 MCP」「如何改 PromptBuilder」的任务清单。后续实现计划必须等知识底座文档通过验收后再写。

- [ ] **H0.3：禁止 DATC64 中心化**  
  DATC64 只能作为项目工作流样例之一。文档主体必须覆盖 POE Studio 的全项目闭环：profile、索引、预览、编辑、overlay、迁移、补丁构建、Native/GGPK、Agent/MCP。

- [ ] **H0.4：必须区分事实、推断、未知项**  
  来自代码、测试、文档、验收报告的内容写入「已确认事实」。根据上下文推导但未直接验证的内容写入「合理推断」。无法确定的内容写入「当前未知项」。禁止把推断写成事实。

- [ ] **H0.5：必须按用户工作流组织**  
  文档不能只是代码文件清单。必须回答用户实际怎样使用项目，以及 Agent 收到自然语言任务时应该如何理解、确认和调用工具。

- [ ] **H0.6：计划可追踪**  
  执行者必须逐项更新本计划复选框，或另建执行记录说明每个任务完成情况、证据来源、未完成原因。

---

## 1. 交付文件

### 创建或补充

- `docs/agent/poe-studio-project-workflows.md`  
  最终知识底座文档。它要成为后续 Agent 项目认知层、prompt context、MCP resource 或知识检索入口的事实来源。

### 只读资料

- `AGENTS.md`
- `docs/ai-project-memory.md`
- `docs/agent/poe-studio-agent-context.md`
- `docs/superpowers/plans/2026-05-22-poe-codex-agent-roadmap.md`
- `docs/superpowers/plans/2026-05-22-poe-mcp-stage1.md`
- `docs/superpowers/plans/2026-05-22-poe-codex-bridge-stage2.md`
- `docs/superpowers/reports/2026-05-22-poe-mcp-stage1-acceptance.md`
- `docs/superpowers/reports/2026-05-22-poe-codex-bridge-stage2-acceptance.md`
- `src/PoeStudio.Api/Program.cs`
- `src/PoeStudio.Api/AgentRoutes.cs`
- `src/PoeStudio.Api/wwwroot/app.js`
- `src/PoeStudio.Contracts/*.cs`
- `src/PoeStudio.Core/**`
- `src/PoeStudio.Storage/**`
- `src/PoeStudio.Mcp/**`
- `tests/PoeStudio.Tests/**`

---

## 2. 目标文档结构

最终文档必须使用以下结构。如果确需调整标题，可以追加章节，但不得删除这些核心章节：

```markdown
# POE Studio 项目工作流与 Agent 知识底座

## 1. 文档目标
## 2. 信息来源与可信度
## 3. 项目总览
## 4. 用户完整工作闭环
## 5. Workspace 与 Profile 模型
## 6. 资源索引与资源读取
## 7. 原始层、草稿层与当前工作态
## 8. 预览、编辑与 Overlay 写入
## 9. DATC64 表格工作流样例
## 10. Overlay 审核与补丁构建
## 11. 迁移、导入与跨 Profile 工作流
## 12. Native / GGPK / Oodle 工作流
## 13. Agent 与 MCP 当前架构
## 14. 工具与 API 地图
## 15. Agent 自然语言任务理解流程
## 16. 风险、审批与权限边界
## 17. 项目术语表
## 18. 已确认事实
## 19. 合理推断
## 20. 当前未知项
## 21. 后续 Agent 知识底座接入方向
```

---

## 3. 任务 1：读取现有项目记忆与阶段资料

**文件：**
- 读取：`AGENTS.md`
- 读取：`docs/ai-project-memory.md`
- 读取：`docs/agent/poe-studio-agent-context.md`
- 读取：`docs/superpowers/plans/2026-05-22-poe-codex-agent-roadmap.md`
- 读取：`docs/superpowers/plans/2026-05-22-poe-mcp-stage1.md`
- 读取：`docs/superpowers/plans/2026-05-22-poe-codex-bridge-stage2.md`
- 读取：`docs/superpowers/reports/2026-05-22-poe-mcp-stage1-acceptance.md`
- 读取：`docs/superpowers/reports/2026-05-22-poe-codex-bridge-stage2-acceptance.md`
- 写入：`docs/agent/poe-studio-project-workflows.md`

- [ ] **步骤 1：确认资料存在**

运行：

```powershell
Test-Path AGENTS.md
Test-Path docs\ai-project-memory.md
Test-Path docs\agent\poe-studio-agent-context.md
Test-Path docs\superpowers\plans\2026-05-22-poe-codex-agent-roadmap.md
Test-Path docs\superpowers\plans\2026-05-22-poe-mcp-stage1.md
Test-Path docs\superpowers\plans\2026-05-22-poe-codex-bridge-stage2.md
Test-Path docs\superpowers\reports\2026-05-22-poe-mcp-stage1-acceptance.md
Test-Path docs\superpowers\reports\2026-05-22-poe-codex-bridge-stage2-acceptance.md
```

预期：全部返回 `True`。如果某个文件缺失，在最终文档「当前未知项」记录缺失资料及影响。

- [ ] **步骤 2：提取项目目标与阶段边界**

从上述资料提取并写入目标文档：

- POE Studio 的项目定位。
- 当前 1+2+3 Agent 路线。
- Stage 1 / Stage 2 / Stage 3 的边界。
- 当前已知的 Agent 失败根因：项目知识缺失、读取层语义缺失、工具语义不足。

- [ ] **步骤 3：写入信息来源与可信度章节**

在 `docs/agent/poe-studio-project-workflows.md` 中新增「信息来源与可信度」表格，至少包含：

| 来源 | 用途 | 可信度 |
| --- | --- | --- |
| 代码 | 当前实现事实 | 高 |
| 测试 | 已覆盖行为 | 高 |
| 验收报告 | 阶段验收证据 | 中高 |
| 计划文档 | 目标与约束 | 中 |
| 对话发现 | 当前真实问题背景 | 中 |

---

## 4. 任务 2：梳理项目总览与用户完整工作闭环

**文件：**
- 读取：`docs/ai-project-memory.md`
- 读取：`src/PoeStudio.Api/Program.cs`
- 读取：`src/PoeStudio.Api/wwwroot/app.js`
- 读取：`tests/PoeStudio.Tests/ApiSmokeTests.cs`
- 写入：`docs/agent/poe-studio-project-workflows.md`

- [ ] **步骤 1：梳理总体闭环**

必须写清楚以下闭环：

```text
检测客户端
→ 保存 profile
→ 建立资源索引
→ 搜索资源
→ 预览资源
→ 编辑文本/结构/表格
→ 写入 overlay 草稿
→ 审核 overlay / diff / audit
→ dry-run / readiness
→ 构建补丁
→ 安装 / 回滚 / 沙盒验证
```

- [ ] **步骤 2：梳理前端工作台入口**

从 `app.js` 和 `index.html` 中识别主要工作区：

- profile / workspace 设置。
- 资源搜索与资源树。
- 预览和编辑区。
- 表格工作区。
- overlay 草稿列表。
- 迁移和补丁构建区。
- Agent 入口只作为当前架构事实，不把 UI 设计作为本次中心。

- [ ] **步骤 3：写入「项目总览」和「用户完整工作闭环」章节**

要求用用户能理解的业务顺序写，不要按类名堆砌。

---

## 5. 任务 3：梳理 Workspace、Profile、资源索引与读取层

**文件：**
- 读取：`src/PoeStudio.Core/Workspace/**`
- 读取：`src/PoeStudio.Storage/Profiles/**`
- 读取：`src/PoeStudio.Storage/Resources/**`
- 读取：`src/PoeStudio.Core/Resources/**`
- 读取：`src/PoeStudio.Api/Program.cs`
- 测试参考：`tests/PoeStudio.Tests/ProfileStoreTests.cs`
- 测试参考：`tests/PoeStudio.Tests/WorkspaceLayoutTests.cs`
- 测试参考：`tests/PoeStudio.Tests/ResourceIndexStoreTests.cs`
- 写入：`docs/agent/poe-studio-project-workflows.md`

- [ ] **步骤 1：梳理 Workspace 与 Profile**

必须覆盖：

- workspace root 如何确定。
- profile 保存在哪里。
- profile 包含哪些关键路径与平台信息。
- profileId 与 display name 的区别。
- Official / WeGame / Bundles2 / GGPK / Oodle 的基本关系。

- [ ] **步骤 2：梳理资源索引**

必须覆盖：

- `ResourceIndexStore` 的职责。
- `ResourceSummaryDto` 的关键字段。
- `VirtualPath` / `NormalizedPath` / `PhysicalPath` 的区别。
- `ResourceKind` 如何影响预览、编辑、补丁构建。
- 索引缺失时 Agent 应如何理解：不是任务失败终点，而是需要引导建立索引。

- [ ] **步骤 3：梳理读取层语义**

必须覆盖：

- Base / 原始层。
- Overlay / Draft / 草稿层。
- PreferOverlay / 当前工作态。
- 哪些现有 API 默认 PreferOverlay。
- 哪些 MCP 工具当前只读 base。

要求明确写出：Agent 如果不知道读取层，就不能生成写入候选。

---

## 6. 任务 4：梳理预览、编辑、Overlay 与表格工作流

**文件：**
- 读取：`src/PoeStudio.Api/Program.cs`
- 读取：`src/PoeStudio.Api/wwwroot/app.js`
- 读取：`src/PoeStudio.Core/Preview/**`
- 读取：`src/PoeStudio.Core/Tables/**`
- 读取：`src/PoeStudio.Storage/Overlay/**`
- 测试参考：`tests/PoeStudio.Tests/OverlayStoreTests.cs`
- 测试参考：`tests/PoeStudio.Tests/OverlayReviewServiceTests.cs`
- 测试参考：`tests/PoeStudio.Tests/TableInspectorTests.cs`
- 测试参考：`tests/PoeStudio.Tests/FrontendDatc64WorkflowTests.cs`
- 写入：`docs/agent/poe-studio-project-workflows.md`

- [ ] **步骤 1：梳理预览与编辑类型**

必须覆盖：

- 普通文本预览。
- 大文本编辑。
- 结构化文本编辑。
- 表格检查。
- CSV 导入导出。
- DAT / DATC64 编辑。
- 保存 overlay 的共同边界。

- [ ] **步骤 2：梳理 Overlay**

必须覆盖：

- `OverlayStore` 保存什么。
- overlay manifest 的作用。
- diff / audit / review / revert 的作用。
- overlay 与补丁构建的关系。

- [ ] **步骤 3：梳理 DATC64 样例工作流**

只作为样例，必须覆盖：

- 目标 profile 与来源 profile。
- 目标表与参考表。
- 跨 profile 对比。
- 可编辑列。
- 当前草稿层。
- 人工审核。
- approval 后写 overlay。

同时必须写明：DATC64 不是 Agent 的边界，只是验证 Agent 是否理解项目工作流的一个样例。

---

## 7. 任务 5：梳理补丁构建、迁移导入、Native/GGPK/Oodle

**文件：**
- 读取：`src/PoeStudio.Core/Patching/**`
- 读取：`src/PoeStudio.Core/Native/**`
- 读取：`src/PoeStudio.Core/Oodle/**`
- 读取：`src/PoeStudio.Api/Program.cs`
- 读取：`src/PoeStudio.Storage/**`
- 测试参考：`tests/PoeStudio.Tests/PatchBuildServiceTests.cs`
- 测试参考：`tests/PoeStudio.Tests/PatchImportAnalyzerTests.cs`
- 测试参考：`tests/PoeStudio.Tests/PatchZipImportServiceTests.cs`
- 测试参考：`tests/PoeStudio.Tests/PatchOverlayDraftServiceTests.cs`
- 测试参考：`tests/PoeStudio.Tests/Native*Tests.cs`
- 写入：`docs/agent/poe-studio-project-workflows.md`

- [ ] **步骤 1：梳理补丁构建闭环**

必须覆盖：

- dry-run。
- readiness。
- Native patch plan。
- build。
- verify。
- install。
- uninstall / rollback。
- sandbox validate。

- [ ] **步骤 2：梳理迁移与导入**

必须覆盖：

- migration plan 的用途。
- 跨 profile 资源匹配。
- 外部补丁导入。
- overlay draft 生成。
- path hash 与资源索引的关系。

- [ ] **步骤 3：梳理 Native / GGPK / Oodle**

必须覆盖：

- Bundles2 与 GGPK 的区别。
- `_.index.bin` / index cache 的作用。
- Oodle 只来自用户本机。
- Agent 遇到 Native/GGPK/Oodle 失败时应该怎样解释，而不是胡乱认为文件不可读。

---

## 8. 任务 6：梳理 Agent / MCP 当前架构与工具地图

**文件：**
- 读取：`src/PoeStudio.Mcp/**`
- 读取：`src/PoeStudio.Core/Agent/**`
- 读取：`src/PoeStudio.Storage/Agent/**`
- 读取：`src/PoeStudio.Api/AgentRoutes.cs`
- 读取：`src/PoeStudio.Contracts/AgentDtos.cs`
- 测试参考：`tests/PoeStudio.Tests/Agent*Tests.cs`
- 测试参考：`tests/PoeStudio.Tests/Mcp*Tests.cs`
- 写入：`docs/agent/poe-studio-project-workflows.md`

- [ ] **步骤 1：梳理 Stage 1 MCP 工具**

必须覆盖当前工具：

- `poe_get_workspace`
- `poe_list_profiles`
- `poe_get_profile`
- `poe_get_index_status`
- `poe_search_resources`
- `poe_read_resource`
- `poe_datc64_extract_translatable_cells`

对每个工具写清：

- 用途。
- 适合什么自然语言任务。
- 关键输入。
- 当前限制。

- [ ] **步骤 2：梳理 Stage 2 Agent runtime**

必须覆盖：

- settings。
- thread。
- message。
- run。
- event。
- plan。
- approval。
- Codex runner。
- PromptBuilder。
- approval 后写入服务。

- [ ] **步骤 3：梳理工具/API 地图**

文档必须列出后续 Agent 可利用的工具/API 类别：

- workspace/profile。
- index/search/read resource。
- preview/table/text。
- overlay/review/revert。
- migration/import。
- patch/build/install。
- jobs。
- agent。
- MCP。

每类写明典型任务与风险边界。

---

## 9. 任务 7：整理 Agent 自然语言任务理解流程

**文件：**
- 写入：`docs/agent/poe-studio-project-workflows.md`

- [ ] **步骤 1：写通用理解流程**

必须写出 Agent 收到用户自然语言后的默认流程：

```text
识别任务意图
→ 识别 profile / resource / layer / action
→ 查询项目上下文
→ 查询实时状态
→ 判断只读、dry-run、写入、构建、安装风险级别
→ 调用工具或提出澄清问题
→ 输出计划/候选/审批点
→ 执行获批动作
→ 记录结果与证据
```

- [ ] **步骤 2：写典型自然语言样例**

至少覆盖：

- “翻译这个表。”
- “帮我找某个资源。”
- “把这个改成草稿。”
- “对比国服和国际服差异。”
- “构建补丁。”
- “为什么失败了。”
- “把外部补丁转成草稿。”
- “继续上次任务。”

每个样例写清 Agent 应先确认什么、调用什么、何时需要问用户。

- [ ] **步骤 3：写风险与审批边界**

至少分为：

| 行为 | 风险级别 | 是否需要审批 |
| --- | --- | --- |
| 查询 profile / 索引状态 | 低 | 否 |
| 读取资源 | 低 | 否 |
| 生成候选 / dry-run | 中 | 否或弱确认 |
| 写 overlay draft | 中高 | 是 |
| 批量写入 | 高 | 是 |
| 构建补丁 | 高 | 是 |
| 安装 / 回滚 | 高 | 是 |
| 删除 / 清理 | 高 | 是 |

---

## 10. 任务 8：整理术语表、事实、推断、未知项与后续接入方向

**文件：**
- 写入：`docs/agent/poe-studio-project-workflows.md`

- [ ] **步骤 1：写项目术语表**

至少包含：

- profile
- workspace
- target / 目标
- source / 来源
- resource
- virtual path
- normalized path
- physical path
- index
- base / 原始层
- overlay / draft / 草稿层
- current working state / 当前工作态
- patch
- migration
- DATC64
- GGPK
- Bundles2
- Oodle
- approval
- dry-run
- readiness

- [ ] **步骤 2：写已确认事实**

要求事实必须能追溯到代码、测试、已有文档或验收报告。示例：

- 前端表格检查通过 `/api/tables/inspect`。
- `/api/tables/inspect` 当前使用 PreferOverlay 读取。
- MCP DATC64 提取工具当前没有 overlay 参数。
- Stage 2 已有 thread/run/event/approval 存储。

- [ ] **步骤 3：写合理推断**

示例：

- “继续翻译当前表”默认应理解为基于目标当前工作态继续处理。
- 来源表默认更接近参考层，除非用户明确指定来源草稿层。

- [ ] **步骤 4：写当前未知项**

至少包含：

- UI 当前选中 profile/resource 是否有稳定后端上下文接口。
- 后续 Agent 是否应该通过 MCP resource、prompt 注入或知识检索读取本文。
- 所有 DATC64 表是否共享同一套可编辑列规则。
- 哪些操作需要更细的审批等级。

- [ ] **步骤 5：写后续接入方向**

只能写方向，禁止写实现任务。必须覆盖：

- 项目上下文文档如何成为 Agent 启动前知识。
- MCP resource 或 project context endpoint 的可能性。
- Agent preflight 如何读取上下文。
- 新知识如何持续沉淀。

---

## 11. 任务 9：自检与交付记录

**文件：**
- 修改：`docs/agent/poe-studio-project-workflows.md`
- 修改：本计划复选框或附加执行记录

- [ ] **步骤 1：跑偏检查**

确认最终文档没有出现以下问题：

- 把 DATC64 写成中心。
- 写成 Agent 修复计划。
- 写成代码实现任务。
- 只罗列代码文件，不讲用户工作流。
- 没有未知项。
- 没有工具/API 地图。
- 没有自然语言任务理解流程。

- [ ] **步骤 2：证据检查**

文档中关键断言必须能指向至少一种证据来源：

- 代码路径。
- 测试路径。
- 项目记忆。
- 阶段计划。
- 验收报告。
- 当前真实问题发现。

- [ ] **步骤 3：最终交付说明**

在最终回复中说明：

- 创建或修改的文档路径。
- 已覆盖的项目工作流。
- 明确未做功能代码修改。
- 仍需人工或 Codex 验收的未知项。

---

## 12. 验收标准

- [ ] `docs/agent/poe-studio-project-workflows.md` 存在。
- [ ] 文档覆盖全项目工作流，不是 DATC64 专项文档。
- [ ] 文档能让不了解 POE Studio 的 AI 理解主要业务闭环。
- [ ] 文档能解释为什么当前 Agent 会误解 DATC64 翻译任务。
- [ ] 文档能支撑后续写「Agent 项目认知层」实现计划。
- [ ] 文档包含工具/API 地图。
- [ ] 文档包含自然语言任务理解流程。
- [ ] 文档包含风险与审批边界。
- [ ] 文档包含项目术语表。
- [ ] 文档包含已确认事实、合理推断、当前未知项。
- [ ] 未修改功能代码。
- [ ] 未新增 Stage 2/Stage 3 实现计划。

