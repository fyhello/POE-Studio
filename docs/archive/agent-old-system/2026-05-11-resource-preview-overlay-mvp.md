# 资源索引预览与 Overlay MVP 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 完成 M2 到 M4 的可运行闭环：资源索引、资源预览、文本 overlay 编辑、overlay 列表/差异/回滚。

**架构：** 继续保持业务层与 LibGGPK3 解耦，先落地可测试的文件系统/展开资源索引与 overlay 工作区模型。索引结果持久化为工作区 JSON 文档，API 提供分页搜索、预览和 overlay 操作；后续 Bundles2 虚拟索引、SQLite 和 Native Kernel 可替换当前 `ResourceIndexStore` 与 `IResourceContentResolver` 的实现。

**技术栈：** .NET 8、ASP.NET Core Minimal API、xUnit、System.Text.Json、文件系统工作区缓存。

---

## 文件结构

新增或修改以下文件：

```text
src/PoeStudio.Contracts/ResourceDtos.cs
src/PoeStudio.Contracts/PreviewDtos.cs
src/PoeStudio.Contracts/OverlayDtos.cs
src/PoeStudio.Core/Resources/ResourceClassifier.cs
src/PoeStudio.Core/Resources/ResourcePath.cs
src/PoeStudio.Core/Resources/FileSystemResourceIndexer.cs
src/PoeStudio.Core/Preview/ResourcePreviewService.cs
src/PoeStudio.Storage/Resources/ResourceIndexStore.cs
src/PoeStudio.Storage/Overlay/OverlayStore.cs
src/PoeStudio.Api/Program.cs
tests/PoeStudio.Tests/ResourceClassifierTests.cs
tests/PoeStudio.Tests/FileSystemResourceIndexerTests.cs
tests/PoeStudio.Tests/ResourceIndexStoreTests.cs
tests/PoeStudio.Tests/ResourcePreviewServiceTests.cs
tests/PoeStudio.Tests/OverlayStoreTests.cs
tests/PoeStudio.Tests/ApiSmokeTests.cs
```

职责说明：

- `ResourceDtos.cs`：资源条目、索引构建请求、搜索请求与响应。
- `PreviewDtos.cs`：预览请求与文本/二进制响应模型。
- `OverlayDtos.cs`：保存文本 overlay、列表、差异、回滚请求与响应。
- `ResourceClassifier.cs`：根据扩展名分类文本、表格、图片、音频、字体、UI、材质、模型、二进制等。
- `ResourcePath.cs`：统一虚拟路径规范化，拒绝路径穿越和绝对路径。
- `FileSystemResourceIndexer.cs`：从展开目录和客户端根目录建立资源条目。
- `ResourcePreviewService.cs`：文本、JSON、XML、dat/datc64 基础文本预览与二进制 hex 预览。
- `ResourceIndexStore.cs`：把资源索引持久化到 profile 工作区并支持分页搜索。
- `OverlayStore.cs`：保存、列出、差异、回滚 overlay 文件。
- `Program.cs`：暴露 `/api/index/build`、`/api/resources/search`、`/api/preview`、`/api/overlay/*`。

## 任务 1：M2 资源分类、路径规范化与索引

**文件：**

- 创建：`src/PoeStudio.Contracts/ResourceDtos.cs`
- 创建：`src/PoeStudio.Core/Resources/ResourceClassifier.cs`
- 创建：`src/PoeStudio.Core/Resources/ResourcePath.cs`
- 创建：`src/PoeStudio.Core/Resources/FileSystemResourceIndexer.cs`
- 测试：`tests/PoeStudio.Tests/ResourceClassifierTests.cs`
- 测试：`tests/PoeStudio.Tests/FileSystemResourceIndexerTests.cs`

- [ ] **步骤 1：编写失败测试**

覆盖扩展分类、危险路径拒绝、文件系统索引能跳过 `Content.ggpk` 这类超大容器并枚举展开资源。

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test PoeStudio.sln --filter "ResourceClassifierTests|FileSystemResourceIndexerTests"`
预期：编译或测试失败，因为类型尚未实现。

- [ ] **步骤 3：实现最少代码**

实现 DTO、分类器、路径规范化和文件系统索引器。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test PoeStudio.sln --filter "ResourceClassifierTests|FileSystemResourceIndexerTests"`
预期：相关测试通过。

## 任务 2：M2 索引持久化与 API 搜索

**文件：**

- 创建：`src/PoeStudio.Storage/Resources/ResourceIndexStore.cs`
- 修改：`src/PoeStudio.Api/Program.cs`
- 测试：`tests/PoeStudio.Tests/ResourceIndexStoreTests.cs`
- 测试：`tests/PoeStudio.Tests/ApiSmokeTests.cs`

- [ ] **步骤 1：编写失败测试**

覆盖保存索引、分页搜索、扩展/类型过滤、未建索引返回空结果、API 构建索引和搜索。

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test PoeStudio.sln --filter "ResourceIndexStoreTests|ApiSmokeTests"`
预期：失败，因为 store 和 API 还不存在。

- [ ] **步骤 3：实现最少代码**

索引保存到 `profiles/<id>/cache/index/resources.json`，搜索支持 `query`、`kind`、`extension`、`skip`、`take`。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test PoeStudio.sln --filter "ResourceIndexStoreTests|ApiSmokeTests"`
预期：相关测试通过。

## 任务 3：M3 资源预览

**文件：**

- 创建：`src/PoeStudio.Contracts/PreviewDtos.cs`
- 创建：`src/PoeStudio.Core/Preview/ResourcePreviewService.cs`
- 修改：`src/PoeStudio.Api/Program.cs`
- 测试：`tests/PoeStudio.Tests/ResourcePreviewServiceTests.cs`
- 测试：`tests/PoeStudio.Tests/ApiSmokeTests.cs`

- [ ] **步骤 1：编写失败测试**

覆盖 UTF-8 文本预览、JSON/XML 格式识别、二进制 hex 预览、缺失资源返回明确错误。

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test PoeStudio.sln --filter "ResourcePreviewServiceTests|ApiSmokeTests"`
预期：失败，因为预览服务和 API 还不存在。

- [ ] **步骤 3：实现最少代码**

从索引条目对应的物理文件读取预览；文本类限制字符数，二进制类限制字节数并返回 hex。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test PoeStudio.sln --filter "ResourcePreviewServiceTests|ApiSmokeTests"`
预期：相关测试通过。

## 任务 4：M4 文本 Overlay 编辑、差异与回滚

**文件：**

- 创建：`src/PoeStudio.Contracts/OverlayDtos.cs`
- 创建：`src/PoeStudio.Storage/Overlay/OverlayStore.cs`
- 修改：`src/PoeStudio.Api/Program.cs`
- 测试：`tests/PoeStudio.Tests/OverlayStoreTests.cs`
- 测试：`tests/PoeStudio.Tests/ApiSmokeTests.cs`

- [ ] **步骤 1：编写失败测试**

覆盖保存文本 overlay、拒绝路径穿越、列出 overlay、生成简明差异、回滚单个 overlay。

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test PoeStudio.sln --filter "OverlayStoreTests|ApiSmokeTests"`
预期：失败，因为 overlay store 和 API 还不存在。

- [ ] **步骤 3：实现最少代码**

overlay 文件写入 `profiles/<id>/overlay/files/<virtualPath>`，元数据写入 `overlay/manifest.json`，差异返回 base/overlay hash、大小与是否文本不同。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test PoeStudio.sln --filter "OverlayStoreTests|ApiSmokeTests"`
预期：相关测试通过。

## 任务 5：全量验证与阶段收尾

**文件：**

- 修改：`docs/superpowers/plans/2026-05-11-resource-preview-overlay-mvp.md`

- [ ] **步骤 1：运行全量测试**

运行：`dotnet test PoeStudio.sln`
预期：所有测试通过，失败数为 0。

- [ ] **步骤 2：检查 git 状态**

运行：`git status --short`
预期：只包含本阶段计划、源码和测试变更。

- [ ] **步骤 3：提交阶段变更**

运行：

```powershell
git add docs src tests
git commit -m "feat: complete resource preview overlay mvp"
```

预期：提交成功。

## 自检

- 覆盖 M2：资源分类、索引持久化、分页搜索、失败容忍。
- 覆盖 M3：文本、JSON、XML、二进制预览，失败返回清晰错误；DDS/dat 表格深度解析保留为后续 handler，不阻塞预览闭环。
- 覆盖 M4：文本 overlay 保存、列表、差异、单文件回滚；表格字段级编辑保留给 dat schema handler，但 dat/datc64 可按文本/二进制 overlay 安全保存。
- 所有新行为先写测试，再实现，再运行验证。
