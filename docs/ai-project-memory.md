# POE Studio AI 项目长期记忆

> 本文是后续 AI 助手进入本仓库时的长期项目记忆。开始任何较复杂工作前，先读本文，再用 GitNexus 查询最新符号图谱。

## 项目定位

POE Studio 是一个 Windows 本地 Web 工具，用于 Path of Exile 2 客户端资源索引、预览、编辑、Overlay 管理和补丁包生成。当前 MVP 重点是可审计的本地修改闭环：检测客户端、建立资源索引、搜索资源、预览/编辑文本或表格、写入 profile 级 overlay、执行 dry-run/readiness 检查、生成补丁 zip，并支持 Native Bundles2 方向的正式写入、验证、安装和沙盒检查。

项目不分发 `oo2core.dll`，只在用户本机提供 Oodle 时使用。LibGGPK3 仅作为研究参考，不直接链接或复制源码。

## 技术栈与运行方式

- 运行时：.NET 8，ASP.NET Core Minimal API。
- 前端：`src/PoeStudio.Api/wwwroot` 下的原生 HTML/CSS/JS，无独立构建链。
- 测试：xUnit，`Microsoft.AspNetCore.Mvc.Testing` 覆盖 API 烟测。
- 入口解决方案：`PoeStudio.sln`。
- 本地启动：`.\启动_POE_Studio.ps1` 或 `dotnet run --project src\PoeStudio.Api\PoeStudio.Api.csproj --urls http://localhost:5010`。
- 发布：`.\发布_POE_Studio.ps1`，输出到 `artifacts\POE-Studio` 和 `artifacts\POE-Studio.zip`。
- 验证基线：`dotnet test PoeStudio.sln --no-restore`。

## 仓库结构

- `src/PoeStudio.Api`：Minimal API、依赖注入、静态前端、后台 job 包装、工作区设置。
- `src/PoeStudio.Contracts`：API DTO、枚举和统一 `ApiResponse<T>`。
- `src/PoeStudio.Core`：纯业务逻辑，包括客户端检测、资源分类、预览、表格/DATC64、Native 解析和补丁构建。
- `src/PoeStudio.Storage`：本地文件持久化，包括 profile、资源索引、overlay、表格 schema、批处理模板和迁移计划。
- `tests/PoeStudio.Tests`：核心服务与 API 回归测试。
- `docs/superpowers`：历史规格与实现计划。
- `docs/ai-project-memory.md`：本文，作为 AI 长期记忆主入口。

## 分层边界

`PoeStudio.Contracts` 不依赖其他项目，定义所有 API 边界类型。`PoeStudio.Core` 依赖 Contracts，承载可测试业务规则。`PoeStudio.Storage` 依赖 Contracts 和 Core，负责文件系统读写。`PoeStudio.Api` 组合三者，负责 HTTP 输入输出、服务编排、静态文件和 job 生命周期。

后续改动尽量保持这个边界：不要把 HTTP 细节塞进 Core，不要让 Storage 承担复杂业务判断，不要在前端绕过 API 直接假设工作区文件结构。

## 工作区与 profile 模型

`WorkspaceRootProvider` 管理当前 workspace 根目录，默认在 `%LOCALAPPDATA%\PoeStudio`，可通过 `/api/workspace` 修改，并保存到 `workspace-settings.json`。所有用户数据都在 workspace 下按 profile 隔离。

`WorkspaceLayout.ForProfile(workspaceRoot, profileId)` 是目录约定的唯一权威入口，会校验 profileId 只能是安全路径片段。目录布局如下：

- `profiles/{profileId}/profile.json`：客户端 profile。
- `profiles/{profileId}/cache/raw`：原始资源、Native index cache 等。
- `profiles/{profileId}/cache/preview`：预览缓存。
- `profiles/{profileId}/cache/table-schemas`：表格 schema。
- `profiles/{profileId}/cache/batch-scripts`：批处理模板。
- `profiles/{profileId}/cache/migration-plans`：迁移计划。
- `profiles/{profileId}/overlay/files`：overlay 实际文件。
- `profiles/{profileId}/overlay/manifest.json`：overlay 清单。
- `profiles/{profileId}/builds`：补丁构建产物、zip、安装备份和清单。
- `profiles/{profileId}/audit`：操作审计。

涉及虚拟资源路径时统一使用 `ResourcePath.Normalize` 和 `ResourcePath.ToSafePhysicalPath`，它们会拒绝绝对路径、`..`、空路径段和冒号，避免路径穿越。

## 客户端检测

`ClientDetector.Detect` 识别客户端根目录：

- 存在 `Content.ggpk` 时，入口类型是 `ClientEntryKind.Ggpk`。
- 存在 `Bundles2/_.index.bin` 时，入口类型是 `ClientEntryKind.Bundles2`。
- 路径包含 `WeGame` 或 `rail_apps` 时推断为国服平台，否则默认 Official。
- `OodleDetector` 查找 `oo2core.dll`，结果保存在 profile 的 `OodleStatus` 和 `OodlePath`。
- client fingerprint 由根路径、`Content.ggpk` 和 index 文件签名散列生成。

API 上 `/api/profiles/detect` 只检测，`/api/profiles/detect-and-save` 会保存 profile，`/api/profiles` 支持手动创建。

## 资源索引

资源索引最终写入 `ResourceIndexStore`，统一形成 `ResourceSummaryDto`：

- `VirtualPath` / `NormalizedPath` 是小写安全虚拟路径。
- `Kind` 由 `ResourceClassifier` 按扩展名分类：Text、Table、Image、Audio、Font、Ui、Material、Model、Binary。
- `PhysicalPath` 可是普通文件路径，也可以是 `native-bundles2://...#offset=...&size=...` 或 `ggpk://...#offset=...&size=...`。
- `SourceLayer` 通常是 Base，后续可能有 Patch、Overlay、Cache。

`ResourceIndexStore` 当前使用 128 个 shard 加 sorted index 和 extension index，搜索上限 `MaxSearchTake = 5000`。它同时实现 `IPatchBundleResourceLookup` 和 `IPatchPathHashLookup`，所以补丁构建和导入也依赖它来定位资源路径、Bundle 记录和 path hash。改动资源索引时要同时考虑搜索性能、补丁构建和补丁导入。

索引来源有三类：

- `FileSystemResourceIndexer`：普通目录资源。
- `NativeBundles2ResourceIndexer`：把解析后的 Native index records 和路径解析结果映射成资源。
- `GgpkResourceIndexer`：读取 GGPK 目录与 FILE 记录，并尝试展开内嵌 Bundles2 index。

## Native Bundles2 与 Oodle

Native 相关代码在 `src/PoeStudio.Core/Native` 和 `src/PoeStudio.Core/Patching`：

- `NativeIndexCacheService` 负责探测、解压和缓存 `_.index.bin`。
- `NativeIndexRecordParser` 解析 bundle/file/directory records。
- `NativeIndexPathResolver` 根据目录记录和 MurmurHash64A 解析虚拟路径。
- `NativeBundleResourceContentResolver` 从 `native-bundles2://` 或 `ggpk://` 资源位置读取真实内容。
- `NativeBundles2PackageWriter` 写正式 Bundles2 补丁包。
- `NativeIndexRewriteDryRun`、`NativeIndexDryWriter`、`NativeDryBundleWriter` 生成 dry-run 产物。
- `NativeOodleCodec` / `NativeOodleCompressCodec` 封装 `oo2core.dll`；`MissingOodleCodec` 和 `CopyNativeBundleCodec` 用于缺失 Oodle 或测试路径。

正式 `NativeBundles2` 构建要求：有 overlay、有资源索引、能定位原始 Native Bundles2 位置、有解压后的真实 index cache，并且 Oodle 可用或请求显式提供 `OodlePath`。测试中 `__copy__` 是 copy codec 路径，不代表真实压缩。

## 预览与读取资源

`ResourcePreviewService` 负责资源预览：

- 文本资源返回文本预览，支持 UTF-8、UTF-16LE 等检测。
- 二进制资源返回 hex 预览。
- PNG、OGG 等可返回媒体预览。
- DDS、Atlas、UI、字体、贴图等会生成 inspection 摘要。
- 大型 CSD 文本会按前端策略打开大文本编辑器。

API 读取资源时经常分为 base 和 overlay 两种路径：`ReadResourceBytesAsync`、`ReadResourceBytesPreferOverlayAsync`、`ReadResourceBytesWithSourceAsync` 等辅助函数在 `Program.cs` 中。后续修预览问题时，要先确认当前读取的是 base 还是 overlay。

## Overlay 模型

`OverlayStore` 是 overlay 的中心：

- `SaveTextAsync` 会根据 base 文件编码写文本 overlay，尽量保留 UTF-16LE BOM 等编码信息。
- `SaveBytesAsync` 写二进制 overlay。
- `ListAsync` 会刷新 manifest 中的 overlay size/hash。
- `SyncExternalAsync` 可从 `overlay/files.txt` 或扫描 `overlay/files` 同步外部 overlay。
- `DiffAsync`、`AuditAsync`、`RevertAsync` 支持对比、审计和撤销。
- `OverlayStore` 同时实现 `IPatchOverlayReader`、`IPatchOverlayWriter`、`IOverlayReviewReader`，被补丁构建、补丁导入草稿和 Overlay Review 复用。

overlay 保存的是“相对虚拟路径对应的一份替换内容”，补丁构建只看 overlay manifest 中的 entries。

## 补丁构建闭环

`PatchBuildService` 是补丁闭环核心：

- `DryRunAsync` 从 overlay entries 构造 `PatchChangeDto`，统计资源类型与风险等级。
- `CheckReadinessAsync` 做 writer、Oodle、真实 index cache、资源索引和 overlay 数量检查。
- `PlanNativePatchAsync` 为每个 overlay 分配目标 bundle、offset、size。
- `PlanNativeIndexRewriteAsync` 用资源索引定位原始 bundle/offset/size，并计算 path hash。
- `BuildNativeDryBundleAsync` 输出 dry bundle、native index plan、native index dry bin 和可选 rewrite dry-run。
- `BuildAsync` 调用 writer 生成真实构建目录、`patch_manifest.json`、`rollback_manifest.json` 和公开 zip。
- `VerifyBuildAsync` 校验 Native Bundles2 输出。
- `InstallAsync` / `UninstallAsync` 支持把构建产物复制到客户端 Bundles2 并维护安装备份。
- `ValidateInSandboxAsync` / `PrepareSandboxAsync` 用于沙盒验证。

writer 类型：

- `MvpPatchPackageWriter`：`OverlayBundleMvp`，当前审计闭环的保守输出。
- `NativeBundles2PackageWriter`：正式 Bundles2 写入。
- `UnavailablePatchPackageWriter`：用于尚未接入的 writer。

补丁 zip 的公开内容只包含可安装的 `Bundles2` 文件，不包含内部 manifest。内部 manifest 保留在 build 目录。

## 补丁导入与迁移

补丁 zip 导入相关类：

- `PatchImportAnalyzer`：分析 zip 结构、识别 manifest、Bundle 文件和潜在补丁内容。
- `PatchZipImportService`：导入 zip。
- `PatchZipInstallPreviewService`：预览安装影响。
- `PatchOverlayDraftService`：把导入的 patch bundle records 转成 overlay 草稿，依赖 `ResourceIndexStore` 通过 path hash 找回虚拟路径。

资源迁移相关 DTO 在 `ResourceExportDtos.cs`，实现主要在 `Program.cs` 的 `BuildMigrationPlan`、`BuildMigrationDraftAsync`、`ApplySavedMigrationPlanAsync`、`ValidateSavedMigrationPlanAsync` 和 `ApplyMigrationItemAsync`。它用于跨 profile 或跨客户端版本匹配资源、生成候选迁移、保存计划并应用到 overlay。

## 表格与 DATC64

`TableInspector` 是高风险核心，文件很大，负责：

- 文本文档表格检测和 CSV 读写。
- 二进制 DAT/DATC64 探测。
- 内置 catalog schema 和用户保存 schema。
- DATC64 string pool、指针列、UTF-8/UTF-16 字符串候选和行结构推断。
- 安全编辑：跳过内部 id、文件路径、非字符串列和不安全 markup 改动。
- Legacy DAT 处理。

`TableSchemaInferer` 用于从 schema 文本/结构推断 `TableSchemaDto`。`TableSchemaStore` 按 profile 持久化 schema。前端 DATC64 对比编辑大量依赖 `app.js` 中的 ag-grid 和 TSV fallback 逻辑。

修改 `TableInspector` 前必须跑：

- `dotnet test PoeStudio.sln --no-restore --filter FullyQualifiedName~TableInspectorTests`
- 涉及前端表格交互时再跑 `FrontendDatc64WorkflowTests` 和相关 API smoke 测试。

## 前端工作台

前端是 `src/PoeStudio.Api/wwwroot/index.html`、`styles.css`、`app.js`：

- `app.js` 单文件集中管理 `state`，没有模块打包。
- `api()` 是前端请求封装。
- profile、workspace、资源搜索、预览、Overlay、迁移、补丁流水线、表格编辑都共享 `state`。
- 大文本编辑使用本地 CodeMirror bundle：`vendor/codemirror/poe-codemirror.js`。
- DATC64 对比优先使用 ag-grid 本地 bundle，也有 TSV 虚拟表格 fallback。
- 前端测试主要通过 `ApiSmokeTests` 检查 HTML 控件存在，通过 `FrontendDatc64WorkflowTests` 检查 JS 逻辑片段。

修改前端时注意：不要引入需要构建的新依赖；保持 DOM id、按钮状态、`state` 字段和 API DTO 名称同步；大函数旁路改动很容易破坏迁移或表格工作流。

## 关键 API 区域

`Program.cs` 很长，集中注册 Minimal API。常见路由分组：

- `/api/health`、`/api/workspace`、`/api/diagnostics`
- `/api/profiles/*`
- `/api/index/build`
- `/api/resources/*`
- `/api/preview`、`/api/resources/preview`
- `/api/text/chunk*`
- `/api/overlay/*`
- `/api/translation/*`
- `/api/batch/*`
- `/api/tables/*`
- `/api/patch/*`
- `/api/jobs/*`
- `/api/native/bundles2/*`
- `/api/native/ggpk/*`

新增 API 时优先复用 `ApiResponse<T>`，失败返回稳定 `errorCode`。如果改动响应 shape，先查前端 `app.js` 和 `ApiSmokeTests` 中的消费者。

## 后台 job

`InMemoryJobStore` 用于长任务包装，前端通过 `trackJob(jobId)` 轮询 `/api/jobs/{jobId}`。Native 索引、GGPK 索引、补丁构建、补丁导入、迁移计划应用等都有 job 入口。它是进程内存态，不是持久队列。

## 测试地图

- `ApiSmokeTests`：API 和首页控件的高层回归，覆盖大量端到端 HTTP 行为。
- `PatchBuildServiceTests`：dry-run、readiness、Native writer、verify、install、uninstall、sandbox。
- `PatchPackageVerifierTests`、`PatchImportAnalyzerTests`、`PatchZipImportServiceTests`、`PatchOverlayDraftServiceTests`：补丁导入/验证。
- `OverlayStoreTests`、`OverlayReviewServiceTests`：overlay 保存、同步、审计、复核。
- `ResourceIndexStoreTests`、`ResourcePreviewServiceTests`、`ResourceClassifierTests`：资源索引、搜索、预览、分类。
- `Native*Tests`、`GgpkResourceIndexerTests`：Native index、Bundle 压缩/解压、路径解析、GGPK。
- `TableInspectorTests`、`TableSchemaInferer` 相关测试：表格和 DATC64。
- `FrontendDatc64WorkflowTests`：前端 DATC64 工作流字符串级回归。
- `ProfileStoreTests`、`WorkspaceLayoutTests`：profile 和目录安全。
- `ProductPackagingTests`：发布脚本和打包形态。

一般改动先跑针对性 filter，再跑完整 `dotnet test PoeStudio.sln --no-restore`。

## 高风险区域

- `src/PoeStudio.Api/Program.cs`：API 编排过长，局部修改可能影响多个路由。
- `src/PoeStudio.Api/wwwroot/app.js`：单文件前端状态复杂，修改 UI 行为要查同名状态和渲染函数。
- `src/PoeStudio.Core/Tables/TableInspector.cs`：DATC64 二进制结构和字符串池处理复杂，必须用测试保护。
- `src/PoeStudio.Core/Patching/PatchBuildService.cs`：补丁构建、安装、回滚和沙盒验证的核心。
- `src/PoeStudio.Core/Patching/NativeBundles2PackageWriter.cs` 与 Native index rewrite 相关类：可能影响真实客户端 Bundles2 产物。
- `src/PoeStudio.Storage/Resources/ResourceIndexStore.cs`：搜索、补丁导入、Native path hash 都依赖它。
- `src/PoeStudio.Storage/Overlay/OverlayStore.cs`：overlay manifest/hash/audit 是构建输入。

## AI 工作约定

1. 开始任务先读本文；如果涉及代码理解，优先用 GitNexus `query`、`context`、`impact`。
2. 修改函数、类、方法前，按 `AGENTS.md` 的 GitNexus 规则跑 impact analysis。
3. 涉及 API response shape 时，检查 `app.js` 和 `ApiSmokeTests`。
4. 涉及 profile 文件布局时，先看 `WorkspaceLayout`，不要手写目录约定。
5. 涉及虚拟路径时，必须用 `ResourcePath`，不要自行拼接。
6. 涉及 Native Bundles2 时，确认 Oodle、index cache、resource index 和 path hash 的关系。
7. 涉及 DATC64 时，先读 `TableInspectorTests` 中已有用例，再做最小修改。
8. 完成前至少运行针对性测试；声称完成前用最新命令输出作证据。
9. 所有代码行为必须有计划、有进度、可追溯、可跟踪。代码创建、代码修改、会改变项目状态的脚本执行、工具生成、迁移、批量操作和自动化都必须先有可见计划；执行中要维护进度状态；偏离计划时先更新计划或留下带日期的说明；高风险动作要记录用户批准点。
10. Agent 相关工作必须按 `docs/superpowers/plans/2026-05-22-poe-codex-agent-roadmap.md` 的 1+2+3 阶段路线推进，并读取当前阶段详细计划。本文只提供长期项目记忆，不替代阶段计划，也不能作为跳过 Stage 1/Stage 2/Stage 3 门禁的依据。Stage 1 是 `POE Studio MCP Tools`，Stage 2 是后端 Codex Bridge Agent runtime，Stage 3 才是 IDE-like Agent Workspace UI。禁止再用工具入口、硬编码工具选择、计划展示或单一 DATC64 闭环冒充 Agent；DATC64 只能作为首个受控能力样例，不能替代全量项目助手方向。

## GitNexus 入口

当前仓库已索引为 `POE-Studio`：

- `gitnexus://repo/POE-Studio/context`
- `gitnexus://repo/POE-Studio/clusters`
- `gitnexus://repo/POE-Studio/processes`

如果 GitNexus 提示索引过期，在仓库根目录运行：

```powershell
npx gitnexus analyze
```
