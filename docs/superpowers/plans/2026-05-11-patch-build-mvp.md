# 补丁构建 MVP 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 完成 M5 的安全构建闭环：dry-run 修改清单、补丁输出目录、Official/Epic/WeGame zip 模板、manifest、回滚信息和 API 入口。

**架构：** 不把 LibGGPK3/LibBundle3 作为当前业务层硬依赖。M5 MVP 使用 overlay 内容生成可审计的补丁包结构、`Tiny.V0.1.bundle.bin` 占位容器和 `_.index.bin` 复制/占位策略，同时在 manifest 中明确 `BuildMode=OverlayBundleMvp`，为后续 Native Kernel 或 LibGGPK3 Adapter 替换真正的 bundle/index 写入器预留接口。

**技术栈：** .NET 8、ASP.NET Core Minimal API、xUnit、System.IO.Compression、System.Text.Json。

---

## 文件结构

```text
src/PoeStudio.Contracts/PatchBuildDtos.cs
src/PoeStudio.Core/Patching/PatchRiskClassifier.cs
src/PoeStudio.Core/Patching/PatchBuildService.cs
src/PoeStudio.Storage/Overlay/OverlayStore.cs
src/PoeStudio.Api/Program.cs
tests/PoeStudio.Tests/PatchRiskClassifierTests.cs
tests/PoeStudio.Tests/PatchBuildServiceTests.cs
tests/PoeStudio.Tests/ApiSmokeTests.cs
```

职责说明：

- `PatchBuildDtos.cs`：dry-run、构建请求/响应、manifest、修改项、平台模板枚举。
- `PatchRiskClassifier.cs`：根据扩展名判定低/中/高风险。
- `PatchBuildService.cs`：读取 overlay，生成 dry-run，构建输出目录、双文件、manifest、rollback、zip。
- `OverlayStore.cs`：暴露读取 overlay 条目的方法，供构建服务使用。
- `Program.cs`：新增 `/api/patch/dry-run` 和 `/api/patch/build`。

## 任务 1：补丁风险分类和 DTO

- [ ] **步骤 1：编写失败测试**

覆盖 `.txt/.json/.xml` 低风险，`.dat/.datc64/.ui` 中风险，`.mat/.ao/.aoc/.pet/.hlsl/.fxgraph` 高风险。

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test PoeStudio.sln --filter "PatchRiskClassifierTests"`
预期：失败，因为类型尚未实现。

- [ ] **步骤 3：实现最少代码**

新增 DTO 和风险分类器。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test PoeStudio.sln --filter "PatchRiskClassifierTests"`
预期：相关测试通过。

## 任务 2：dry-run 和构建服务

- [ ] **步骤 1：编写失败测试**

覆盖无 overlay 时 dry-run 为空、存在 overlay 时按类型和风险汇总、build 生成 `Bundles2/_.index.bin`、`Bundles2/Tiny.V0.1.bundle.bin`、`patch_manifest.json`、`rollback_manifest.json` 和 zip。

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test PoeStudio.sln --filter "PatchBuildServiceTests"`
预期：失败，因为服务尚未实现。

- [ ] **步骤 3：实现最少代码**

构建模式为 `OverlayBundleMvp`：`_.index.bin` 优先复制 profile 的 `IndexPath`，不存在则写入空占位；`Tiny.V0.1.bundle.bin` 写入 overlay 文件清单和内容块，manifest 记录这是 MVP 容器，不声称可直接被游戏加载。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test PoeStudio.sln --filter "PatchBuildServiceTests"`
预期：相关测试通过。

## 任务 3：补丁 API

- [ ] **步骤 1：编写失败测试**

API 覆盖创建 profile、保存 overlay、dry-run、build，确认返回输出 zip 路径和 manifest 路径。

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test PoeStudio.sln --filter "ApiSmokeTests"`
预期：失败，因为 API 尚未实现。

- [ ] **步骤 3：实现最少代码**

注册 `PatchBuildService`，新增 `/api/patch/dry-run` 和 `/api/patch/build`。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test PoeStudio.sln --filter "ApiSmokeTests"`
预期：相关测试通过。

## 任务 4：全量验证与提交

- [ ] **步骤 1：运行全量测试**

运行：`dotnet test PoeStudio.sln`
预期：全部测试通过，失败数 0。

- [ ] **步骤 2：检查 git 状态**

运行：`git status --short`
预期：只包含本阶段计划、源码和测试变更。

- [ ] **步骤 3：提交阶段变更**

运行：

```powershell
git add docs src tests
git commit -m "feat: add patch build mvp"
```

预期：提交成功。

## 自检

- 覆盖 M5 的 dry-run、双文件输出、平台 zip 模板、manifest、回滚信息。
- 明确当前是 `OverlayBundleMvp`，不假装已经完成真实 Bundles2 index 重写。
- 保留后续接入 Native Kernel/LibGGPK3 Adapter 的替换空间。
