# POE Studio 项目工作流与 Agent 知识底座

> 本文用于帮助后续 Agent 理解 POE Studio 的项目语义、用户工作流、工具边界和审批风险。它是知识底座，不是 Stage 2 / Stage 3 修复计划，也不是 DATC64 专项方案。

## 1. 文档目标

本文回答 3 个问题：

- POE Studio 用户真实怎样完成「检测客户端 → 索引资源 → 编辑草稿 → 审核 → 构建补丁 → 安装/回滚」闭环。
- Agent 收到自然语言任务时，应该怎样识别 profile、resource、读取层、写入边界和审批点。
- 后续把本文接入 Agent prompt、MCP resource 或项目上下文检索时，哪些内容可以当作事实，哪些只是推断或未知项。

本文不提供功能实现步骤，不要求改代码，不替代阶段计划。DATC64 只作为一个代表性样例，用来验证 Agent 是否理解 profile、表格、草稿层、来源参考和审批边界。

## 2. 信息来源与可信度

| 来源 | 用途 | 可信度 |
| --- | --- | --- |
| 代码 | 当前实现事实，例如路由、DTO、存储路径和服务职责 | 高 |
| 测试 | 已覆盖行为，例如 API smoke、MCP、Overlay、Patch、Native、Table、Agent 测试 | 高 |
| 验收报告 | Stage 1 / Stage 2 真实验收证据 | 中高 |
| 计划文档 | 目标、阶段边界、硬约束和禁止事项 | 中 |
| 对话发现 | 当前真实问题背景，例如 Agent 对 DATC64 读取层的误解 | 中 |
| GitNexus 图谱 | 模块边界、执行流和影响面辅助索引 | 中高 |

主要证据入口：

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

## 3. 项目总览

POE Studio 是一个 Windows 本地 Web 工具，用于 Path of Exile 2 客户端资源索引、预览、编辑、Overlay 管理、补丁构建、补丁导入和本地安装验证。它运行在 .NET 8 / ASP.NET Core Minimal API 上，前端是 `src/PoeStudio.Api/wwwroot` 下的原生 HTML/CSS/JS，没有独立前端构建链。

代码分层如下：

| 层 | 职责 |
| --- | --- |
| `PoeStudio.Contracts` | DTO、枚举和统一 API 契约 |
| `PoeStudio.Core` | 纯业务逻辑：客户端检测、资源分类、预览、表格、Native、Oodle、补丁、Agent prompt/runner/parser |
| `PoeStudio.Storage` | 本地文件持久化：profile、资源索引、overlay、迁移计划、表格 schema、批处理模板、Agent store |
| `PoeStudio.Api` | Minimal API、依赖注入、静态前端、后台 job、工作区设置 |
| `PoeStudio.Mcp` | Stage 1 MCP stdio server，向 Codex 暴露只读 POE Studio 工具 |

产品核心不是「翻译某个表」，而是 profile 隔离下的可审计本地修改闭环。用户先把一个客户端保存成 profile，建立资源索引，再在 UI 中搜索、预览、编辑资源。编辑不会直接改游戏文件，而是写入该 profile 的 overlay draft。后续补丁构建只读取 overlay manifest，并通过 dry-run、readiness、Native plan、build、verify、install、rollback、sandbox validate 等步骤控制风险。

## 4. 用户完整工作闭环

POE Studio 的典型用户闭环是：

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

用户视角的业务顺序：

1. 设置 workspace，并检测或手动创建客户端 profile。
2. 根据客户端形态建立资源索引：普通文件、Native Bundles2 或 GGPK。
3. 在资源搜索、资源树和格式扫描中找到目标资源。
4. 预览文本、媒体、二进制摘要、结构化文本或表格。
5. 对可编辑资源写入 overlay draft，而不是直接改 base 资源。
6. 通过 overlay list、diff、audit、review 观察所有草稿。
7. 通过 dry-run 和 readiness 判断补丁构建是否可行。
8. 构建补丁 zip 或 Native Bundles2 产物。
9. 可选执行 verify、install、uninstall、sandbox prepare / validate。

前端工作台入口主要来自 `index.html` 和 `app.js`：

- workspace / profile 设置：`/api/workspace`、`/api/profiles/*`。
- 资源搜索与资源树：`/api/resources/search`、`/api/resources/by-path`、format scan。
- 预览和编辑区：`/api/preview`、`/api/resources/preview`、`/api/text/chunk*`、structured inspect/save。
- 表格工作区：`/api/tables/inspect`、CSV 导入导出、schema 推断、DAT/DATC64 编辑。
- overlay 草稿列表：`/api/overlay/list`、sync、diff、audit、review、revert。
- 迁移和补丁构建区：migration plan/draft/apply、patch dry-run/readiness/build/install。
- Agent 入口：当前已有 Stage 2 后端 API，但正式 Agent Workspace UI 属于 Stage 3。

## 5. Workspace 与 Profile 模型

workspace root 由 `WorkspaceRootProvider` 管理。项目记忆记录默认位置是 `%LOCALAPPDATA%\PoeStudio`，也可以通过 `/api/workspace` 修改，并保存到 `workspace-settings.json`。

profile 是客户端配置和所有衍生数据的隔离单位。`WorkspaceLayout.ForProfile(workspaceRoot, profileId)` 是 profile 目录约定的权威入口，会校验 `profileId` 只能是安全路径片段。目录结构包括：

| 路径 | 含义 |
| --- | --- |
| `profiles/{profileId}/profile.json` | profile 元数据 |
| `profiles/{profileId}/cache/raw` | 原始资源和 Native index cache |
| `profiles/{profileId}/cache/preview` | 预览缓存 |
| `profiles/{profileId}/cache/table-schemas` | 表格 schema |
| `profiles/{profileId}/cache/batch-scripts` | 批处理模板 |
| `profiles/{profileId}/cache/migration-plans` | 迁移计划 |
| `profiles/{profileId}/overlay/files` | overlay draft 实际文件 |
| `profiles/{profileId}/overlay/manifest.json` | overlay manifest |
| `profiles/{profileId}/builds` | 构建产物、zip、安装备份、manifest |
| `profiles/{profileId}/audit` | 审计记录 |

`ClientProfileDto` 的关键字段包括：

- `Id`：profile 的稳定标识，用于路径和 API 参数。
- `DisplayName`：用户可读名称，例如「国际服-目标」或「国服-简体来源」。
- `Platform`：Official、WeGame、Steam、Epic、Unknown 等平台语义。
- `EntryKind`：普通目录、Bundles2、GGPK 等客户端入口形态。
- `RootPath`、`ContentGgpkPath`、`Bundles2Path`、`IndexPath`：客户端资源定位。
- `OodleStatus` 和相关路径：说明用户本机是否提供 Oodle。
- `ClientFingerprint`：用于识别客户端版本/来源。

Official / WeGame 描述平台来源；Bundles2 / GGPK 描述资源入口；Oodle 是解压/压缩 Native bundle 时可能需要的用户本机依赖。Agent 不能把它们混为一谈。

## 6. 资源索引与资源读取

`ResourceIndexStore` 负责保存、搜索和定位资源索引。它当前使用 shard 结构和 legacy index 兼容路径，搜索上限在 store 中被限制。它同时实现补丁构建/导入需要的资源查找接口，例如 path hash 和 bundle name 查询。

`ResourceSummaryDto` 的关键字段：

| 字段 | 含义 |
| --- | --- |
| `VirtualPath` | 用户和 API 看到的虚拟资源路径 |
| `NormalizedPath` | 统一小写/规范化后的安全虚拟路径 |
| `PhysicalPath` | 实际来源，可是普通文件路径，也可是 `native-bundles2://...` 或 `ggpk-bundles2://...` |
| `Kind` | Text、Table、Image、Audio、Font、Ui、Material、Model、Binary 等 |
| `SourceLayer` | Base、Patch、Overlay、Cache |
| `IndexedAt` | 索引时间 |

路径语义：

- `VirtualPath` 用于用户表达、UI 显示和资源匹配。
- `NormalizedPath` 用于比较、overlay manifest、索引查找和路径安全。
- `PhysicalPath` 是读取 bytes 的位置，不一定是文件系统路径。

`ResourceKind` 影响默认处理方式。Text、Table、Ui 常可预览和编辑；Image、Audio、Font 更偏预览；Binary 通常只能 hex/摘要预览；Native/GGPK 资源还需要 Native resolver 和 Oodle 语义。

索引缺失不是任务失败终点。Agent 应解释为：当前 profile 还没有可查询资源清单，需要引导用户建立索引，或调用已有 index build 入口，而不是直接宣称资源不存在。

## 7. 原始层、草稿层与当前工作态

这是 Agent 最容易误解的核心。

| 层 | 含义 | 典型入口 |
| --- | --- | --- |
| Base / 原始层 | 索引指向的客户端原始资源 | `ReadResourceBytesAsync`、MCP resource reader |
| Overlay / Draft / 草稿层 | 用户已编辑但尚未构建成补丁的替换文件 | `OverlayStore`、`profiles/{profileId}/overlay/files` |
| 当前工作态 | 优先读取草稿层；没有草稿才回退原始层 | `ReadResourceBytesPreferOverlayAsync`、多处 UI 默认 |

已确认代码事实：

- `ReadResourceBytesPreferOverlayAsync` 先查 `OverlayStore.GetEntriesAsync`，命中 overlay 文件时读 overlay，否则回退 `ReadResourceBytesAsync`。
- `/api/tables/inspect` 总是通过 `ReadResourceBytesPreferOverlayAsync` 读取表格。
- `/api/tables/save` 也先通过 `ReadResourceBytesPreferOverlayAsync` 读取当前内容，再应用编辑并写入 overlay。
- 前端 `state.previewUseOverlay` 默认是 `true`，预览、导出、签名、迁移、批处理等多处请求都会带 `useOverlay`。
- `poe_read_resource` 和 `poe_datc64_extract_translatable_cells` 是 MCP 只读工具。当前代码已支持 indexed physical、`native-bundles2://`、`ggpk-bundles2://` 资源读取边界，但工具语义仍属于 Stage 1 只读能力，不负责写 overlay。

### MCP 当前读取层限制

当前 MCP 工具可以读取索引资源，但工具参数没有显式的 `useOverlay`、`preferOverlay` 或 `readLayer` 字段，也不会通过 `OverlayStore` 解析目标 profile 的当前草稿层。也就是说，MCP 读取工具适合回答「base 资源是什么」「索引里能否找到这个资源」「这个资源的只读内容摘要是什么」，但不能单独代表 UI 中的「当前工作态」。

这会直接影响写入候选生成。若用户任务依赖当前草稿，例如「继续翻译当前表」「只补没翻译的单元格」，Agent 不能只依赖 `poe_read_resource` 或 `poe_datc64_extract_translatable_cells` 得出候选。它必须先确认读取层，或通过后端 overlay-aware API / 后续扩展工具读取目标当前工作态，再生成 proposal。

Agent 如果不知道读取层，就不能正确生成写入候选。用户说「继续翻译当前表」「补全当前草稿」「只处理还没翻译的单元格」时，默认目标读取层应是当前工作态，而不是目标 base。

## 8. 预览、编辑与 Overlay 写入

预览类型：

- 普通文本预览：`ResourcePreviewService` 读取文本、检测编码并返回预览。
- 大文本编辑：`/api/text/chunk` 和 `/api/text/chunk/save` 分块读取/保存。
- 结构化文本编辑：structured inspect/save 识别 key/value 类节点并写 overlay。
- 表格检查：`/api/tables/inspect` 调用 `TableInspector`。
- CSV 导入导出：`/api/tables/export-csv`、`/api/tables/import-csv*`。
- DAT / DATC64 编辑：`TableInspector` 处理 DATC64 string pool、候选列、安全编辑和 legacy DAT。
- 替换文件：`/api/overlay/save-file` 将上传文件写为 overlay。

Overlay 共同边界：

- 保存目标是 profile 的 overlay draft，不是游戏客户端原始文件。
- `OverlayStore.SaveTextAsync` 尽量保留 base 文本编码，例如 UTF-16LE BOM。
- `OverlayStore.SaveBytesAsync` 写二进制 overlay。
- overlay manifest 保存虚拟路径、overlay 路径、hash、size、base hash、时间戳。
- `OverlayStore.ListAsync` 会刷新 manifest 中的 size/hash。
- diff / audit / review / revert 用于审查和撤销草稿。

Overlay 与补丁构建的关系：补丁构建不扫描 UI 状态，而是读取 overlay manifest 中的 entries。没有 overlay，就没有可构建的用户修改。

## 9. DATC64 表格工作流样例

DATC64 是样例，不是 Agent 的边界。

典型跨 profile 对比工作流：

1. 用户选择目标 profile，例如「国际服-目标」。
2. 用户打开目标表，例如 `data/balance/traditional chinese/activeskills.datc64`。
3. UI 通过 `/api/tables/inspect` 读取目标当前工作态，默认优先草稿层。
4. UI 根据语言目录和 profile 配对匹配来源 profile，例如「国服-简体来源」。
5. UI 读取参考表，例如 `data/balance/simplified chinese/activeskills.datc64`。
6. 前端渲染左右对比：左侧国服参考，右侧国际服目标。
7. 可编辑列来自 inspect 结果中的 `editableColumnIndexes` 及前端渲染规则。
8. 用户人工审核差异，复制/应用参考或手动编辑目标侧。
9. 保存时调用 `/api/tables/save`，写入目标 profile 的 overlay draft。
10. 后续补丁构建只看目标 profile 的 overlay manifest。

Agent 处理类似「翻译这个表」任务时，需要确认：

- 目标 profile 与来源 profile 分别是谁。
- 目标表与参考表的虚拟路径。
- 目标读取层是当前工作态还是原始层。
- 来源读取层是参考层还是来源草稿层。
- 哪些列可编辑。
- 是否需要跳过目标与来源相同、两边都是英文、空值、数字、路径、hash、不可编辑列。
- 写入是否已获得 approval。

当前 Agent 误解 DATC64 翻译任务的根因不是单纯 prompt 不够详细，而是 Agent 项目知识中缺少「目标当前工作态」和「跨 profile 参考表」语义。若只读目标 base，就会把用户已经在草稿层完成的翻译再次作为候选。

## 10. Overlay 审核与补丁构建

补丁构建闭环由 `PatchBuildService` 组织，输入主要来自 overlay manifest。

关键步骤：

- `DryRunAsync`：把 overlay entries 转成变更清单，统计资源类型和风险等级。
- `CheckReadinessAsync`：检查 writer、Oodle、Native index cache、资源索引和 overlay 数量。
- `PlanNativePatchAsync`：给 overlay 分配目标 bundle、offset、size。
- `PlanNativeIndexRewriteAsync`：根据资源索引定位原始 bundle/offset/size，并计算 path hash。
- `BuildNativeDryBundleAsync`：生成 dry bundle、Native index plan、Native index dry bin 和可选 rewrite dry-run。
- `BuildAsync`：生成真实构建目录、manifest、rollback manifest 和公开 zip。
- `VerifyBuildAsync`：校验 Native Bundles2 输出。
- `InstallAsync` / `UninstallAsync`：复制或移除构建产物，并维护安装备份。
- `PrepareSandboxAsync` / `ValidateInSandboxAsync`：准备和校验沙盒环境。

writer 类型：

- `MvpPatchPackageWriter`：保守的 OverlayBundleMvp 输出。
- `NativeBundles2PackageWriter`：正式 Native Bundles2 写入。
- `UnavailablePatchPackageWriter`：未接入 writer 的明确失败路径。

对 Agent 来说，dry-run 和 readiness 是写入前后的风险解释工具，不等于用户已经批准安装或回滚。

## 11. 迁移、导入与跨 Profile 工作流

迁移用于跨 profile 或跨客户端版本处理资源差异。典型用途包括：从来源 profile 匹配目标 profile，生成迁移建议，保存方案，校验方案，将安全项或候选项写为目标 overlay draft。

相关 API 覆盖：

- `/api/resources/match`
- `/api/resources/migration-plan`
- `/api/resources/migration-draft`
- `/api/resources/migration-apply-item`
- `/api/resources/migration-plans/save`
- `/api/resources/migration-plans/list`
- `/api/resources/migration-plans/load`
- `/api/resources/migration-plans/validate`
- `/api/resources/migration-plans/apply`

外部补丁导入流程包括：

- `PatchImportAnalyzer` 分析 zip 结构、manifest、Bundles 文件和潜在补丁内容。
- `PatchZipImportService` 导入 zip 到 profile build 区。
- `PatchZipInstallPreviewService` 预览安装影响。
- `PatchOverlayDraftService` 将导入的 patch bundle records 转成 overlay draft。

path hash 与资源索引的关系：Native index 中的 file record 可通过 path hash 与 `ResourceIndexStore` 中的 normalized path 对齐。外部补丁导入转草稿时，如果无法从 path hash 找回虚拟路径，就无法安全写入 overlay draft。

## 12. Native / GGPK / Oodle 工作流

Bundles2 与 GGPK 的区别：

- Bundles2 profile 通常直接有 `Bundles2/_.index.bin` 和 bundle 文件。
- GGPK profile 以 `Content.ggpk` 为入口，可能包含内嵌 Bundles2 index 和 bundle data。
- 普通 physical resource 是文件系统路径；Native/GGPK resource 的 `PhysicalPath` 可能是 `native-bundles2://...` 或 `ggpk-bundles2://...`。

`_.index.bin` 和 index cache 的作用：

- `NativeIndexCacheService` 探测、解压并缓存 Native index。
- `NativeIndexRecordParser` 解析 bundle/file/directory records。
- `NativeIndexPathResolver` 结合目录记录和 MurmurHash64A 解析虚拟路径。
- `NativeBundleResourceContentResolver` 读取 Native/GGPK 定位到的真实内容。

Oodle 边界：

- POE Studio 不分发 `oo2core.dll`。
- `OodleDetector` 只在用户本机路径中查找 Oodle。
- 真实 Native Bundles2 构建通常需要可用 Oodle，或请求显式提供 `OodlePath`。
- 测试里的 copy codec 不等于真实压缩。

Agent 遇到 Native/GGPK/Oodle 失败时，应解释为「需要 Native 读取服务、index cache 或用户本机 Oodle」，而不是简单说文件不可读或资源不存在。

## 13. Agent 与 MCP 当前架构

Stage 1 MCP 工具当前包括：

| 工具 | 用途 | 适合的自然语言任务 | 关键输入 | 读取层 / overlay 支持 | 当前限制 |
| --- | --- | --- | --- | --- | --- |
| `poe_get_workspace` | 返回 workspace root、来源、数据目录、当前进程目录 | “当前工作区是什么？” | 无 | 不涉及资源读取层 | 只读 |
| `poe_list_profiles` | 列出 profile id、显示名、客户端类型 | “有哪些客户端配置？” | 可选 workspace 上下文 | 不涉及资源读取层 | 只读 |
| `poe_get_profile` | 获取单个 profile 详情 | “这个 profile 指向哪里？” | `profileId` | 不涉及资源读取层 | 只读 |
| `poe_get_index_status` | 查看索引是否存在、资源数、更新时间 | “索引建好了吗？” | `profileId` | 不读取资源内容 | 不会自动建立索引 |
| `poe_search_resources` | 在已索引资源中搜索 | “帮我找某个资源。” | `profileId`、`query`、`limit` | 查询索引记录，不读取 overlay 文件 | 不扫描磁盘 |
| `poe_read_resource` | 读取索引资源内容摘要 | “读取这个资源看看。” | `profileId`、`resourcePath`、`maxBytes`、`oodlePath` | 读取索引资源内容；当前无 `useOverlay` / `preferOverlay` 参数，不代表 UI 当前工作态 | 只读；受 maxBytes 和 Native/Oodle 约束 |
| `poe_datc64_extract_translatable_cells` | 提取 DATC64 或 string-candidate 可翻译单元 | “提取表里的可翻译文本。” | `profileId`、`resourcePath`、`limit`、`oodlePath` | 基于 MCP 只读资源读取边界；当前无 overlay-aware 参数，不能判断目标草稿层已有翻译 | 只读；不写 overlay；不替代跨 profile 对比 |

Stage 2 Agent runtime 当前包含：

- settings：Codex path、model、profile、sandbox、MCP server name、working directory、approval mode。
- thread：用户任务会话。
- message：用户/助手/系统消息。
- run：一次 Agent 执行。
- event：Codex stdout/stderr、MCP tool call、Agent message、approval、overlay write、failure、cancel。
- plan：run 的计划步骤。
- approval：写入前审批记录。
- Codex runner：`codex exec --json` 子进程。
- PromptBuilder：根据能力、上下文、历史消息、MCP 工具生成 prompt。
- approval 后写入服务：当前 DATC64 draft 由后端在 approval 后写 overlay。

当前能力：

| taskKind | 含义 | 写入 |
| --- | --- | --- |
| `question` | 通用只读问答 | 不允许 |
| `read-only-analysis` | 资源/项目只读分析 | 不允许 |
| `datc64-translation` | DATC64 翻译 proposal | approval 后后端写 overlay |

## 14. 工具与 API 地图

| 类别 | 典型 API / 工具 | 典型任务 | 风险边界 |
| --- | --- | --- | --- |
| workspace/profile | `/api/workspace`、`/api/profiles/*`、MCP profile tools | 配置工作区、检测客户端、保存 profile | 删除 profile 属高风险；检测和查询低风险 |
| index/search/read resource | `/api/index/build`、`/api/resources/search`、MCP search/read | 建索引、找资源、读资源 | 建索引改变 cache；读取低风险 |
| preview/table/text | `/api/preview`、`/api/text/chunk*`、`/api/tables/*` | 预览、编辑文本和表格 | 保存会写 overlay，需要确认 |
| overlay/review/revert | `/api/overlay/*` | 审核草稿、diff、audit、撤销 | revert/bulk revert 高风险 |
| migration/import | `/api/resources/migration-*`、`/api/jobs/patch/import-*` | 跨 profile 迁移、外部补丁导入、转草稿 | 写草稿和批量应用需审批 |
| patch/build/install | `/api/patch/*`、`/api/jobs/patch/*` | dry-run、readiness、build、verify、install、rollback、sandbox | build/install/uninstall 高风险 |
| jobs | `/api/jobs/{jobId}` | 跟踪长任务 | job 是进程内状态，不是持久队列 |
| agent | `/api/agent/*` | 创建 thread/run、记录事件、审批写入 | 写入必须 approval |
| MCP | `poe_*` tools | Codex 只读读取项目上下文 | Stage 1 工具只读，不直接写 overlay |

## 15. Agent 自然语言任务理解流程

默认流程：

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

典型样例：

| 用户说法 | Agent 应先确认 | 可调用工具/API | 何时问用户 |
| --- | --- | --- | --- |
| “翻译这个表。” | 目标 profile、来源 profile、目标表、参考表、读取层、可编辑列 | `poe_list_profiles`、`poe_get_index_status`、`poe_search_resources`、`poe_datc64_extract_translatable_cells`、后端 Agent approval | profile/resource/layer 不明确，或要写 overlay 前 |
| “帮我找某个资源。” | profile、关键词、资源类型 | `poe_search_resources` | 多 profile 都可能匹配时 |
| “把这个改成草稿。” | 当前资源、目标文本/文件、是否覆盖已有 overlay | preview/read、overlay save API | 写入内容不明确或已有草稿时 |
| “对比国服和国际服差异。” | source profile、target profile、query/path、是否使用 overlay | resources match、migration plan、table inspect | 来源/目标角色不明确时 |
| “构建补丁。” | 目标 profile、writer、bundleName、Oodle、是否只 dry-run | patch dry-run、readiness、build | build/install 前必须确认 |
| “为什么失败了。” | run/job/build id、错误发生阶段 | job、agent events、patch verification、readiness | 缺少失败对象时 |
| “把外部补丁转成草稿。” | zip 路径、目标 profile、bundleName、Oodle | analyze zip、import zip、patch overlay draft | 写 overlay draft 前确认 |
| “继续上次任务。” | thread/run、当前 profile/resource、上次 plan、pending approvals | Agent thread snapshot、events、overlay list | 存在多个未完成 thread 或 approval 时 |

风险与审批边界：

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

## 16. 风险、审批与权限边界

低风险动作可以直接执行，例如读取 workspace、列 profile、查看索引状态、搜索资源、读取资源摘要。

中风险动作应输出候选和证据，例如 dry-run、迁移建议、翻译 proposal、补丁 readiness。它们不直接改变用户资源，但会影响下一步决策。

中高和高风险动作必须有明确审批点，例如写 overlay、批量写入、构建补丁、安装、回滚、删除 profile 或清理草稿。

Agent 禁止行为：

- 不知道读取层时生成写入候选。
- 未经 approval 写 overlay。
- 把索引缺失当成最终失败而不给出下一步。
- 用固定脚本入口冒充 Agent。
- 把 DATC64 专用流程当成全项目助手。
- 隐藏工具调用、stderr、失败原因或审批状态。

## 17. 项目术语表

| 术语 | 含义 |
| --- | --- |
| profile | 一个客户端配置及其本地缓存、overlay、构建产物的隔离单位 |
| workspace | POE Studio 本地数据根目录 |
| target / 目标 | 用户要修改、构建或安装的 profile/resource |
| source / 来源 | 用于参考、迁移或对比的 profile/resource |
| resource | 资源索引中的一条资源记录 |
| virtual path | 用户可见的虚拟资源路径 |
| normalized path | 规范化后的安全虚拟路径 |
| physical path | 资源实际读取位置，可能是文件路径或 Native/GGPK URI |
| index | profile 下的资源清单和查找结构 |
| base / 原始层 | 客户端原始资源 |
| overlay / draft / 草稿层 | 用户本地已编辑但未构建进补丁的替换文件 |
| current working state / 当前工作态 | 优先 overlay，否则 base 的读取语义 |
| patch | 基于 overlay 构建出的可安装补丁产物 |
| migration | 跨 profile 或版本匹配资源并生成草稿/方案的流程 |
| DATC64 | Path of Exile 数据表二进制格式之一 |
| GGPK | `Content.ggpk` 客户端资源容器 |
| Bundles2 | POE 资源 bundle 形态，通常由 `_.index.bin` 索引 |
| Oodle | 用户本机提供的压缩/解压库，POE Studio 不分发 |
| approval | 写入或高风险动作前的人工批准 |
| dry-run | 不落地写入的预演/风险评估 |
| readiness | 构建或 Native 写入前的可执行性检查 |

## 18. 已确认事实

| 已确认事实 | 证据来源 |
| --- | --- |
| POE Studio 是 .NET 8 / ASP.NET Core Minimal API 本地 Web 工具，前端无独立构建链。 | `docs/ai-project-memory.md`；`src/PoeStudio.Api/Program.cs`；`src/PoeStudio.Api/wwwroot/index.html`；`src/PoeStudio.Api/wwwroot/app.js` |
| workspace/profile 目录约定由 `WorkspaceLayout.ForProfile` 统一定义。 | `src/PoeStudio.Core/Workspace/WorkspaceLayout.cs` |
| `ProfileStore` 将 profile 保存到 `profiles/{profileId}/profile.json`。 | `src/PoeStudio.Storage/Profiles/ProfileStore.cs`；`src/PoeStudio.Core/Workspace/WorkspaceLayout.cs` |
| `ResourceIndexStore` 保存、搜索资源索引，并支持 path hash / bundle name 查找。 | `src/PoeStudio.Storage/Resources/ResourceIndexStore.cs`；`tests/PoeStudio.Tests/ResourceIndexStoreTests.cs` |
| `ResourceSummaryDto` 包含 `VirtualPath`、`NormalizedPath`、`PhysicalPath`、`Kind`、`SourceLayer`。 | `src/PoeStudio.Contracts/ResourceDtos.cs` |
| 前端 `state.previewUseOverlay` 默认是 `true`。 | `src/PoeStudio.Api/wwwroot/app.js` |
| `/api/tables/inspect` 当前使用 `ReadResourceBytesPreferOverlayAsync`。 | `src/PoeStudio.Api/Program.cs` |
| `/api/tables/save` 当前也使用 `ReadResourceBytesPreferOverlayAsync`，然后写 overlay。 | `src/PoeStudio.Api/Program.cs` |
| MCP 资源读取工具当前没有 `useOverlay` / `preferOverlay` / `readLayer` 参数。 | `src/PoeStudio.Mcp/PoeMcpTools.cs`；`src/PoeStudio.Mcp/PoeResourceContentReader.cs` |
| `OverlayStore` 保存 overlay 文件和 manifest，并支持 list、sync external、diff、audit、review、revert。 | `src/PoeStudio.Storage/Overlay/OverlayStore.cs`；`src/PoeStudio.Core/Overlay/OverlayReviewService.cs`；`tests/PoeStudio.Tests/OverlayStoreTests.cs`；`tests/PoeStudio.Tests/OverlayReviewServiceTests.cs` |
| `PatchBuildService` 覆盖 dry-run、readiness、Native plan、build、verify、install、uninstall、sandbox validate/prepare。 | `src/PoeStudio.Core/Patching/PatchBuildService.cs`；`tests/PoeStudio.Tests/PatchBuildServiceTests.cs` |
| `PatchOverlayDraftService` 将补丁 bundle records 转成 overlay draft，并依赖 path hash 查找虚拟路径。 | `src/PoeStudio.Core/Patching/PatchOverlayDraftService.cs`；`tests/PoeStudio.Tests/PatchOverlayDraftServiceTests.cs` |
| Stage 1 MCP 工具已在验收报告中记录为 `Stage 1 status: PASS`。 | `docs/superpowers/reports/2026-05-22-poe-mcp-stage1-acceptance.md` |
| Stage 2 后端 Agent runtime 已在验收报告中记录为 `Stage 2 status: PASS`。 | `docs/superpowers/reports/2026-05-22-poe-codex-bridge-stage2-acceptance.md` |
| Stage 2 支持 `question`、`read-only-analysis`、`datc64-translation` 3 类能力。 | `src/PoeStudio.Core/Agent/AgentCapabilities.cs`；`tests/PoeStudio.Tests/AgentCapabilitiesTests.cs`；Stage 2 验收报告 |
| Stage 2 DATC64 写入必须先产生 approval，批准后由后端写 overlay。 | `src/PoeStudio.Storage/Agent/AgentOrchestrator.cs`；`src/PoeStudio.Storage/Agent/Datc64DraftApplyService.cs`；`tests/PoeStudio.Tests/AgentApiSmokeTests.cs`；`tests/PoeStudio.Tests/Datc64DraftApplyServiceTests.cs` |
| 当前 Agent / MCP 事实不等于正式 Agent Workspace UI，Stage 3 才是 UI 工作台。 | `docs/superpowers/plans/2026-05-22-poe-codex-agent-roadmap.md`；`docs/superpowers/plans/2026-05-22-poe-codex-bridge-stage2.md` |

## 19. 合理推断

- 用户说「继续翻译当前表」时，目标读取层应默认理解为当前工作态，即优先读取目标 overlay draft。
- 来源表默认更接近参考层，除非用户明确要求来源 profile 也使用草稿层。
- 如果用户只说「这个资源」，Agent 应优先从当前 UI/thread/run 上下文中找 profile/resource；找不到再询问。
- 迁移、补丁导入和 DATC64 翻译都共享「候选 → 审核 → 写 overlay → 构建」心智模型。
- Agent preflight 应先看 profile、索引状态和资源存在性，再进入内容读取或 proposal。

## 20. 当前未知项

- UI 当前选中 profile/resource 是否有稳定后端上下文接口，还是仍主要存在前端 `state` 中。
- 后续 Agent 应通过 MCP resource、prompt 注入、project context endpoint，还是知识检索读取本文。
- 所有 DATC64 表是否共享同一套可编辑列规则。
- 哪些操作需要比当前风险表更细的审批等级。
- source profile 的默认读取层是否在所有跨 profile 任务中都应为 base。
- Native/GGPK 读取失败时，Agent 是否需要更结构化的恢复建议 schema。

## 21. 后续 Agent 知识底座接入方向

本文可以作为 Agent 启动前项目知识来源。接入方式可以是 prompt 上下文、MCP resource、后端 project context endpoint 或检索索引；具体方式需要结合 token 成本、更新频率和 Stage 3 UI 形态决定。

Agent preflight 方向：

- 先读取项目上下文摘要。
- 再读取 workspace/profile/index 实时状态。
- 对任务中的 profile、resource、layer、action、risk 做结构化识别。
- 对写入类任务生成候选、审批点和证据。
- 将新的失败案例、验收结论和项目约定持续沉淀回知识底座。

新知识沉淀方向：

- 阶段验收报告继续记录真实命令、输出摘要和边界。
- 代码事实以 DTO、路由和测试作为主要依据。
- 真实用户误解案例单独归档，避免把临时修复写成永久事实。
- 未知项在验证前保持未知，不写入「已确认事实」。
