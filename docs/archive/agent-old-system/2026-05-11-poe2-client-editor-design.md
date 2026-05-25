# POE2 客户端编辑与翻译工具技术设计

## 1. 背景与目标

本项目要构建一个面向《流放之路 2》（Path of Exile 2，以下简称 POE2）的本地客户端资源编辑、翻译、补丁构建与后续自动化工作台。目标不是简单复制现有补丁工具，而是在 `LibGGPK3` 和 `poe2_patch_studio` 的基础经验之上，形成可长期维护、可扩展、可审计、可回滚的客户端编辑系统。

第一阶段聚焦可落地闭环：

- 打开国际服 `Content.ggpk` 和国服/Steam/Epic/WeGame 的 `Bundles2/_.index.bin`。
- 建立百万级资源索引，支持浏览、搜索、分组、预览。
- 支持文本、配置、图片、DDS、二进制、`datc64/dat` 的基础预览。
- 支持安全 overlay 编辑，不直接破坏客户端。
- 支持构建覆盖式双文件补丁：`Bundles2/_.index.bin` 与 `Bundles2/Tiny.V0.1.bundle.bin`。
- 输出 manifest、审计记录、回滚信息。

后续阶段扩展翻译工作台、批处理脚本、特征匹配、旧补丁迁移和 AI 辅助能力。

## 2. 调研结论

### 2.1 参考项目

`LibGGPK3` 的价值在底层容器读写：

- `LibGGPK3` 处理 `Content.ggpk`。
- `LibBundle3` 处理 `Bundles2/*.bundle.bin` 和 `_.index.bin`。
- `LibBundledGGPK3` 组合 GGPK 与 bundle 场景。
- 示例已覆盖提取、替换、压缩、打补丁、中文化等基础链路。

限制与风险：

- 当前许可为 AGPL-3.0-or-later。正式产品不能把它作为不可替代的闭源依赖。
- 项目 README 提到对单个 GGPK 的多线程处理需谨慎。
- 运行 bundle 解析需要 Oodle 运行时（例如 `oo2core.dll`）。

`poe2_patch_studio` 的价值在工作流：

- 本地 Web 工作台。
- 资源浏览、预览、对比、规则、Profile 构建。
- `datc64` 安全编辑、CSD 翻译、审计、manifest。

限制：

- Node 单体服务已经较大，后续继续堆功能会难维护。
- 底层读取、提取、构建散落在 Node、脚本、C# worker、清单文件和 overlay 目录之间。
- 更适合作为功能原型和产品经验参考，而不是长期内核。

### 2.2 真实客户端与补丁结构

国际服客户端路径：

```text
E:\PSAutoRecover\ui\rood\Grinding Gear Games\Path of Exile 2
```

观察结果：

- 主入口是 `Content.ggpk`。
- `Content.ggpk` 约 137.85 GB。
- 存在 `Bundlebak\134213838631151834.index.bin`，说明补丁流程可能会备份 index。

国服客户端路径：

```text
C:\WeGameApps\rail_apps\流放之路：降临(2002052)
```

观察结果：

- 主入口是 `Bundles2/_.index.bin`。
- `Bundles2` 下有大量 `.bundle.bin` 和展开资源目录。
- `_.index.bin` 约 124.95 MB。
- 解析得到约 60751 个 bundle、3414615 个虚拟文件。
- 路径解析存在 5 个失败项，系统必须允许「带警告继续」。

两个现成补丁包的核心内容一致：

```text
Bundles2/_.index.bin
Bundles2/Tiny.V0.1.bundle.bin
```

Epic 版本只是多了外层目录：

```text
PathOfExile2/Bundles2/_.index.bin
PathOfExile2/Bundles2/Tiny.V0.1.bundle.bin
```

当前 `Tiny.V0.1.bundle.bin` 在 index 中关联约 21251 个虚拟资源，主要不是单纯文本表，而包含大量功能、特效、材质和对象资源：

- `metadata/effects`：9443 个。
- `metadata/terrain`：5302 个。
- `metadata/items`：4392 个。
- `metadata/monsters`：765 个。
- `metadata/particles`：651 个。
- 常见扩展包括 `.mat`、`.ao`、`.aoc`、`.pet`、`.tdt`、`.ot`、`.otc`、`.ui`、`.hlsl`、`.fxgraph`。

因此系统必须把「补丁审查」作为一等功能，不能只把补丁理解为汉化文本。

## 3. 产品约束

这些约束优先级高于具体技术实现：

- 常用操作路径要短，常用动作不超过 1 到 2 次点击进入。
- UI 按钮、设置、状态文案必须直白，一眼知道用途。
- 高级设置默认折叠，不干扰日常流程。
- 资源加载必须可缓存、可预热、可分阶段完成。
- 不能出现「点一个资源等半天没有反馈」的体验。
- 所有重任务必须后台 Job 化，提供进度、阶段、日志、取消和重试。
- 大型资源树和表格必须虚拟滚动，不一次性渲染全部数据。
- 危险操作必须 dry-run、确认、审计、可回滚。
- 用户不需要理解 bundle/index 底层结构，也能完成常用补丁流程。
- 专业用户可以展开高级视图查看 bundle、offset、hash、来源、风险。

## 4. 技术路线

采用方案 C 的改良版：

```text
Desktop Shell
  -> React/TypeScript Workbench UI
  -> ASP.NET Core Local API
  -> Resource Kernel Abstractions
      -> LibGGPK3 Adapter（原型与验证）
      -> Native Kernel（正式可替换内核）
  -> SQLite Index DB + Workspace Cache + Overlay
```

核心原则：

- UI 不直接触碰大文件。
- 后端统一负责索引、读取、转换、写入和构建。
- 业务层依赖自己的资源内核接口，不直接依赖 LibGGPK3 类型。
- 原型阶段可用 LibGGPK3 快速验证。
- 正式阶段逐步自研 GGPK/Bundles2 native kernel，降低 AGPL 绑定风险。

## 5. 解决方案目录结构

建议第一阶段目录结构：

```text
src/
  PoeStudio.AppHost/              # 桌面壳或本地启动器，后期可接 WebView2/Tauri/Electron
  PoeStudio.Api/                  # ASP.NET Core 本地 API
  PoeStudio.Contracts/            # DTO、API contract、枚举、错误码
  PoeStudio.Core/                 # 业务用例：项目、索引、资源、编辑、构建
  PoeStudio.Kernel.Abstractions/  # IResourceContainer、IResourceReader 等核心接口
  PoeStudio.Kernel.LibGGPK3/      # LibGGPK3 适配器，仅原型/验证使用
  PoeStudio.Kernel.Native/        # 后续自研内核实现
  PoeStudio.Formats/              # 格式 handler：文本、dat、图片、DDS、UI 等
  PoeStudio.Storage/              # SQLite、文件缓存、manifest、审计
  PoeStudio.Jobs/                 # 后台任务、进度、取消、日志
  PoeStudio.Patching/             # patch overlay、bundle 构建、zip 模板
  PoeStudio.Tests/

web/
  src/
    app/
    pages/
    features/
    components/
    api/
    state/
```

项目边界：

- `Contracts` 只能放跨进程/跨层数据结构，不放业务逻辑。
- `Core` 只依赖抽象接口，不依赖 LibGGPK3。
- `Kernel.LibGGPK3` 是可拔插适配器。
- `Formats` 负责把 byte stream 变成可预览或可编辑模型。
- `Patching` 负责 overlay 到 bundle/index 的构建流程。
- `Storage` 负责持久化，不承载业务决策。

## 6. 核心数据模型

### 6.1 ClientProfile

表示一个客户端实例。

字段：

- `id`
- `displayName`
- `platform`：`Official`、`Epic`、`Steam`、`WeGame`、`Custom`
- `entryKind`：`GGPK`、`Bundles2`
- `rootPath`
- `contentGgpkPath`
- `bundles2Path`
- `indexPath`
- `oodleStatus`
- `clientFingerprint`
- `lastIndexedAt`

### 6.2 ResourceContainer

统一资源容器抽象。

类型：

- `GgpkContainer`
- `BundleIndexContainer`
- `ExtractedDirectoryContainer`
- `PatchZipContainer`
- `OverlayContainer`

能力：

- 枚举资源。
- 读取资源。
- 查询资源元数据。
- 获取资源来源。
- 创建只读快照。

### 6.3 VirtualResource

统一虚拟资源条目。

字段：

- `id`
- `profileId`
- `virtualPath`
- `normalizedPath`
- `extension`
- `resourceKind`
- `size`
- `hash`
- `bundlePath`
- `bundleIndex`
- `offset`
- `sourceLayer`：`Base`、`Patch`、`Overlay`、`Cache`
- `previewState`
- `editableState`
- `riskLevel`
- `parseStatus`

### 6.4 PatchOverlay

所有修改先进入 overlay。

字段：

- `id`
- `profileId`
- `virtualPath`
- `baseHash`
- `overlayHash`
- `overlayPath`
- `editKind`
- `source`：`Manual`、`Rule`、`Batch`、`AI`
- `validationStatus`
- `createdAt`
- `updatedAt`

### 6.5 PatchManifest

一次构建的完整说明。

字段：

- `id`
- `profileId`
- `buildMode`
- `targetPlatform`
- `clientFingerprint`
- `outputRoot`
- `zipPath`
- `changedResourceCount`
- `targetBundleName`
- `indexBeforeHash`
- `indexAfterHash`
- `createdAt`
- `rollbackPath`

### 6.6 BackgroundJob

后台任务模型。

字段：

- `id`
- `kind`
- `status`：`Queued`、`Running`、`Succeeded`、`Failed`、`Canceled`
- `progress`
- `stage`
- `message`
- `startedAt`
- `finishedAt`
- `logPath`
- `cancelRequested`

## 7. SQLite 表设计

第一阶段 SQLite 表：

```text
client_profiles
resource_containers
virtual_resources
resource_stats
resource_dependencies
workspace_overlays
preview_cache_entries
background_jobs
operation_audit
patch_manifests
patch_manifest_items
settings
```

关键索引：

- `virtual_resources(profile_id, normalized_path)` 唯一。
- `virtual_resources(profile_id, extension)`。
- `virtual_resources(profile_id, resource_kind)`。
- `virtual_resources(profile_id, bundle_path)`。
- `workspace_overlays(profile_id, normalized_path)`。
- `background_jobs(status, created_at)`。

百万级资源必须避免频繁全表扫描。搜索第一阶段可以先用前缀/包含索引和分页，后续再接 SQLite FTS。

## 8. API 设计

API 保持短路径、直白命名。

### 8.1 项目与客户端

```text
GET  /api/health
GET  /api/profiles
POST /api/profiles/detect
POST /api/profiles
POST /api/profiles/{profileId}/open
POST /api/profiles/{profileId}/verify
```

### 8.2 索引与资源浏览

```text
POST /api/index/build
GET  /api/index/status
GET  /api/resources/tree
GET  /api/resources/search
GET  /api/resources/{resourceId}
GET  /api/resources/raw
POST /api/resources/extract
```

### 8.3 预览与格式 handler

```text
POST /api/preview
GET  /api/preview/{previewId}
POST /api/table/preview
POST /api/table/model
POST /api/text/read
POST /api/image/preview
```

### 8.4 编辑与 overlay

```text
POST /api/overlay/save-text
POST /api/overlay/save-table
GET  /api/overlay/list
POST /api/overlay/diff
POST /api/overlay/revert
```

### 8.5 构建与应用

```text
POST /api/patch/dry-run
POST /api/patch/build
GET  /api/patch/manifests
GET  /api/patch/manifests/{manifestId}
POST /api/patch/apply
POST /api/patch/rollback
```

### 8.6 后台任务

```text
GET  /api/jobs
GET  /api/jobs/{jobId}
POST /api/jobs/{jobId}/cancel
```

进度推送第一阶段可用轮询，后续改为 Server-Sent Events 或 WebSocket。

## 9. 后台任务设计

所有可能超过 300 ms 的动作都进入 Job：

- 客户端扫描。
- Oodle 检测。
- 索引构建。
- GGPK/Bundles2 读取。
- 资源提取。
- DDS 转换。
- DAT 表解析。
- 批量规则预览。
- 补丁 dry-run。
- 补丁构建。
- zip 打包。
- 回滚包生成。

Job 必须支持：

- 阶段化进度。
- 取消令牌。
- 结构化日志。
- 失败错误码。
- 重试。
- 任务结果持久化。

UI 显示要求：

- 顶部任务中心显示正在运行的任务。
- 资源预览区域显示「正在准备预览」，不能空白卡死。
- 失败时给用户可执行建议，例如「缺少 Oodle，请在设置中指定 oo2core.dll」。

## 10. 缓存与工作区规范

工作区根目录建议：

```text
<workspace>/
  profiles/
    <profileId>/
      index.db
      profile.json
      fingerprints/
      cache/
        raw/
        preview/
        tables/
        images/
      overlay/
        files/
        metadata/
      builds/
        <buildId>/
          Bundles2/
            _.index.bin
            Tiny.V0.1.bundle.bin
          adapted_patch.zip
          patch_manifest.json
          rollback_manifest.json
      audit/
        operations.ndjson
        operations/
```

缓存策略：

- raw cache 按 `profileId + virtualPath + sourceHash` 命名。
- preview cache 按 `resourceHash + previewKind + optionsHash` 命名。
- 表格模型缓存必须带 schema 版本。
- 客户端文件指纹变化后，缓存标记为 stale，不直接删除。

overlay 策略：

- 用户保存只写 overlay，不写原客户端。
- overlay 条目保存 base hash，用于客户端更新后的冲突判断。
- 构建时从 overlay 收集候选资源。
- 支持单文件回滚、批量回滚、按构建回滚。

## 11. 格式 Handler 设计

统一接口：

```text
IResourceFormatHandler
  CanHandle(resource)
  Inspect(bytes, context)
  BuildPreview(bytes, context)
  BuildEditorModel(bytes, context)
  SaveEditorModel(model, context)
  Validate(bytes, context)
```

第一阶段 handler：

- `TextHandler`：`.txt`、`.xml`、`.json`、`.filter`、`.ui` 的基础文本模式。
- `DatHandler`：`.dat`、`.datc64` 的预览和基础编辑。
- `ImageHandler`：`.png`、`.bmp`、`.jpg`。
- `DdsHandler`：DDS 转 PNG/WebP 预览。
- `BinaryHandler`：hex 预览与提取。

后续 handler：

- `AtlasHandler`
- `MaterialHandler`
- `AudioHandler`
- `FontHandler`
- `ModelAnimationHandler`
- `EffectGraphHandler`

未知格式不阻断流程，至少支持二进制预览、提取、替换和风险提示。

## 12. 补丁构建流水线

构建目标：

```text
Bundles2/_.index.bin
Bundles2/Tiny.V0.1.bundle.bin
```

平台 zip 模板：

- Official：`Bundles2/...`
- Epic：`PathOfExile2/Bundles2/...`
- Steam：第一阶段使用 Official 模板，后续单独确认。
- WeGame：优先输出 Bundles2 双文件，是否可直接覆盖需单独验证。

构建步骤：

1. 收集 overlay 修改。
2. 校验 base hash 是否仍匹配当前客户端。
3. 执行 dry-run，输出会修改的虚拟路径、类型、大小、风险。
4. 生成或复用目标 patch bundle。
5. 将修改资源写入 `Tiny.V0.1.bundle.bin`。
6. 重写 `_.index.bin` 中对应资源的 bundle、offset、size。
7. 输出 `patch_manifest.json`。
8. 生成回滚信息。
9. 按平台模板打包 zip。

风险控制：

- dry-run 阶段必须显示按类型分组的修改数量。
- 高风险类型默认要求确认，例如 `.mat`、`.ao`、`.aoc`、`.pet`、`.hlsl`、`.fxgraph`。
- 如果路径解析存在失败项，构建可以继续，但 manifest 必须记录警告。
- 如果 Oodle 缺失，构建直接阻止并给出修复路径。

## 13. 前端页面与操作流

第一阶段页面：

```text
启动页
资源工作台
编辑工作台
补丁构建
任务中心
设置
```

### 13.1 启动页

目标：让用户最快打开客户端。

主按钮：

- `打开客户端`
- `打开补丁包`
- `最近项目`

检测结果直白显示：

- `已识别：官方客户端（Content.ggpk）`
- `已识别：Bundles2 客户端`
- `缺少 Oodle，部分资源无法读取`

### 13.2 资源工作台

布局：

- 左侧：资源树与快速筛选。
- 中间：资源列表与预览。
- 右侧：属性、操作、风险和依赖。

按钮口径：

- `预览`
- `提取`
- `编辑`
- `加入补丁`
- `查看差异`

### 13.3 编辑工作台

按格式进入对应编辑器：

- 文本编辑器。
- 表格编辑器。
- 图片预览与替换。
- 二进制查看。

保存按钮文案：

- `保存到工作区`

避免误解为直接写入客户端。

### 13.4 补丁构建

三步：

1. `检查修改`
2. `生成补丁`
3. `打开输出`

高级选项折叠：

- bundle 名称。
- zip 模板。
- 高风险确认。
- manifest 路径。

## 14. LibGGPK3 Adapter 与 Native Kernel 边界

业务层只依赖这些接口：

```text
IResourceContainer
IResourceIndexReader
IResourceContentReader
IResourcePatchWriter
IBundleIndexWriter
IOodleRuntime
```

`PoeStudio.Kernel.LibGGPK3` 负责：

- 把 LibGGPK3/LibBundle3 类型转换为内部 DTO。
- 处理 Oodle 运行时注入。
- 容忍少量路径解析失败。
- 提供原型期读写能力。

`PoeStudio.Kernel.Native` 后续负责：

- 自研 GGPK record 读取。
- 自研 bundle/index 解析。
- 自研 patch bundle 写入。
- 自研 hash/path 映射。
- 与 LibGGPK3 适配器做行为对照测试。

法律与发布策略：

- 原型期允许使用 LibGGPK3 adapter。
- 如果发布闭源版本，必须替换为 Native Kernel 或取得单独授权。
- 文档中明确区分第三方依赖、适配器代码和自研内核。

## 15. 第一阶段 MVP 里程碑

### M1：客户端识别与项目工作区

完成标准：

- 能选择国际服或国服客户端目录。
- 能识别 `Content.ggpk` 或 `Bundles2/_.index.bin`。
- 能检测 Oodle 状态。
- 能创建 `ClientProfile` 和 workspace。

### M2：资源索引

完成标准：

- 能解析 Bundles2 index。
- 能持久化资源列表到 SQLite。
- 能分页浏览和搜索资源。
- 能容忍少量路径解析失败。

### M3：资源预览

完成标准：

- 文本、JSON、XML、二进制可预览。
- DDS 可生成缓存预览。
- `dat/datc64` 可表格预览。
- 预览失败有明确错误。

### M4：Overlay 编辑

完成标准：

- 文本资源能保存到 overlay。
- 表格资源能保存基础字段改动到 overlay。
- 能查看 overlay 列表和差异。
- 能回滚单个 overlay。

### M5：补丁构建

完成标准：

- dry-run 显示修改清单。
- 能生成 `_.index.bin` 和 `Tiny.V0.1.bundle.bin`。
- 能输出 Official/Epic zip 模板。
- 能生成 manifest 和回滚信息。

## 16. 非目标

第一阶段不做：

- 完整 AI 翻译。
- 完整 `.mat/.ao/.aoc/.pet` 结构化编辑。
- 完整模型/动画编辑。
- 在线分发平台。
- 绕过客户端校验或反作弊相关能力。
- 自动修改正在运行的游戏进程。

这些能力可以在后续阶段基于 handler、batch、AI provider 和 patch manifest 扩展。

## 17. 待验证问题

正式实现前需要继续验证：

- Steam 客户端 zip 根路径是否与 Official 一致。
- WeGame 覆盖双文件是否稳定可用。
- 国际服 `Content.ggpk` 直接写入与生成外置 Bundles2 补丁的兼容边界。
- `Tiny.V0.1.bundle.bin` 命名是否所有平台都安全，是否需要可配置。
- `_.index.high.bin` 和 `_.index.low.bin` 在不同平台是否需要同步更新。
- Oodle DLL 的合法分发方式，是否只能让用户手动指定本地路径。
- `datc64` schema 来源和本地缓存策略。

## 18. 规格自检

- 范围聚焦第一阶段 MVP，没有把 AI、复杂材质编辑、模型编辑放进首期。
- 业务层与 LibGGPK3 解耦，后续可替换 Native Kernel。
- 已把真实补丁结构和百万级资源规模写入约束。
- 已明确 UI 短路径、不卡顿、可回滚等硬性要求。
- 已列出待验证问题，避免把未知平台差异当成已确认事实。
