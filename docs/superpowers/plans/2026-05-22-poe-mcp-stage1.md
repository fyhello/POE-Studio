# POE Studio MCP Stage 1 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 交付一个可被 Codex CLI 真实发现和调用的 `poe-studio` MCP stdio 服务器，让 Codex 读取 POE Studio workspace/profile/index/resource/DATC64 信息，并为后续 Agent 后端打基础。

**架构：** 新增独立 console 项目 `src/PoeStudio.Mcp`，优先使用官方 ModelContextProtocol C# SDK 提供 stdio server、tool listing 和 tool call。只有在 SDK 无法满足 Codex CLI 当前 stdio 接入时，才允许切换到本计划定义的手写 JSON-RPC fallback。工具只读或 dry-run，复用现有 Core/Storage/Contracts 能力，不新增前端、不新增 Agent API、不执行真实 overlay 写入。

**技术栈：** .NET 8、官方 ModelContextProtocol C# SDK、System.Text.Json、xUnit、MCP JSON-RPC stdio、Codex CLI、POE Studio Core/Storage/Contracts。

---

## 0. Stage 1 硬约束

- [ ] **S1-H0.1：本阶段不是完整 Agent**  
  本阶段交付名称必须是 `POE Studio MCP Tools`。文档、UI、API、提交信息不得宣称已经完成完整 Agent。

- [ ] **S1-H0.2：只做 MCP 工具层**  
  允许新增 `src/PoeStudio.Mcp` 和测试。禁止新增 `/api/agent/*`、`/api/codex/*`、Agent Workspace UI、Codex app-server 封装。

- [ ] **S1-H0.3：不写真实业务数据**  
  本阶段所有工具必须是只读或 dry-run。`poe_overlay_prepare_draft` 如实现，只能返回拟写入内容和风险摘要，不得调用现有 overlay 写入服务，不得创建草稿文件。

- [ ] **S1-H0.4：Codex 真实验收优先于单元测试**  
  单元测试通过只是必要条件。没有 `codex mcp add`、`codex mcp list`、`codex exec --json` 真实调用证据，Stage 1 不能 PASS。

- [ ] **S1-H0.5：工作区来源必须明确**  
  MCP 进程按优先级解析 workspace：`--workspace-root` 参数、`POE_STUDIO_WORKSPACE_ROOT` 环境变量、`%LOCALAPPDATA%\PoeStudio\workspace-settings.json`。解析失败时工具返回 `isError: true`，不得猜测路径。

- [ ] **S1-H0.6：协议 stdout 纯净**  
  stdout 只能写 MCP JSON-RPC 消息。日志、诊断、异常摘要必须写 stderr，避免破坏 stdio 协议。

- [ ] **S1-H0.7：官方 SDK 优先**  
  实现者必须先评估并尝试使用官方 `ModelContextProtocol` C# SDK。只有满足以下全部条件时，才允许手写 MCP 协议：1）记录 SDK 包版本；2）记录失败命令和错误；3）确认失败不是用法错误；4）在验收报告中写入 `MCP implementation mode: manual fallback`。

- [ ] **S1-H0.8：资源读取不得伪成功**  
  `poe_read_resource` 和 `poe_datc64_extract_translatable_cells` 必须明确区分 physical resource 与 native Bundles2 resource。Stage 1 如果没有抽取可复用只读 native 读取服务，则 native resource 必须返回 `isError: true` 和下一步建议，不得返回空内容冒充成功。

---

## 1. 文件结构

### 新增文件

- `src/PoeStudio.Mcp/PoeStudio.Mcp.csproj`  
  MCP console 项目，引用 `PoeStudio.Core`、`PoeStudio.Storage`、`PoeStudio.Contracts`。

- `src/PoeStudio.Mcp/Program.cs`  
  入口，解析参数，创建 workspace resolver，注册工具，运行 stdio loop。

- `src/PoeStudio.Mcp/McpProtocol.cs`  
  仅在官方 C# SDK 无法接入时创建。职责为 MCP JSON-RPC 基础协议类型、请求解析、响应写入、错误响应。

- `src/PoeStudio.Mcp/McpToolRegistry.cs`  
  工具元数据、输入 schema、工具调用分发。使用官方 C# SDK 时，该文件作为 POE 工具定义和 handler 集中注册层，不重复实现 SDK 已提供的协议能力。

- `src/PoeStudio.Mcp/PoeWorkspaceResolver.cs`  
  workspace 解析逻辑，支持参数、环境变量、本地设置文件。

- `src/PoeStudio.Mcp/PoeResourceContentReader.cs`  
  Stage 1 只读资源读取边界。支持 indexed physical resource 读取；native Bundles2 resource 如果未能安全复用 `NativeBundleResourceContentResolver`，返回可操作错误，不得伪造成功。

- `src/PoeStudio.Mcp/PoeMcpTools.cs`  
  POE 工具实现：profile、workspace、index、resource、DATC64 提取。

- `tests/PoeStudio.Tests/McpProtocolTests.cs`  
  stdin/stdout 协议级测试。

- `tests/PoeStudio.Tests/McpToolRegistryTests.cs`  
  工具列表、schema、未知工具、参数错误测试。

- `tests/PoeStudio.Tests/McpWorkspaceResolverTests.cs`  
  workspace 来源优先级和失败路径测试。

- `tests/PoeStudio.Tests/McpDatc64ToolTests.cs`  
  DATC64 可翻译单元提取测试。

- `tests/PoeStudio.Tests/McpResourceContentReaderTests.cs`  
  physical resource 读取、路径穿越防护、native resource 明确失败测试。

- `docs/superpowers/reports/2026-05-22-poe-mcp-stage1-acceptance.md`  
  Stage 1 验收报告。

### 修改文件

- `PoeStudio.sln`  
  加入 `PoeStudio.Mcp` 项目。

- `tests/PoeStudio.Tests/PoeStudio.Tests.csproj`  
  引用 `PoeStudio.Mcp` 项目，允许测试协议和工具注册。

---

## 2. MCP 工具清单

### 必须实现

- [ ] `poe_get_workspace`  
  返回 workspace root、解析来源、POE Studio 数据目录路径、当前进程工作目录。

- [ ] `poe_list_profiles`  
  返回 profile id、name、client type、workspace 绑定信息。失败时返回 `isError: true` 和可操作错误。

- [ ] `poe_get_profile`  
  输入 `profileId`，返回单个 profile 详情。

- [ ] `poe_get_index_status`  
  返回索引是否存在、资源数量、索引文件路径、最后更新时间。

- [ ] `poe_search_resources`  
  输入 `query`、`limit`，返回资源路径、类型、大小、来源摘要。`limit` 默认 20，最大 100。

- [ ] `poe_read_resource`  
  输入 `profileId`、`resourcePath`、`maxBytes`，返回文本或 base64/hex 摘要；默认最大 65536 bytes，最大 1048576 bytes。Stage 1 必须支持 indexed physical resource；native Bundles2 resource 如果没有安全读取服务，必须返回 `isError: true` 和 `native_resource_not_supported_in_stage1`。

- [ ] `poe_datc64_extract_translatable_cells`  
  输入 `profileId`、`resourcePath`、`limit`，通过 `poe_read_resource` 同一只读边界获取 bytes，再复用 `TableInspector` 提取 DATC64/string-candidate 中可翻译单元，返回 row、column、sourceText、textEncoding、offset 或可追踪定位信息。

### 可选但仅 dry-run

- [ ] `poe_overlay_prepare_draft`  
  输入候选翻译 JSON，返回拟生成 draft 的摘要、风险、目标资源列表和 diff-like 预览。不得写入 overlay，不得创建文件。

### 明确禁止

- [ ] 禁止 `poe_overlay_apply_draft` 出现在 Stage 1。
- [ ] 禁止 `poe_run_script`、`poe_write_file`、`poe_generate_tool` 出现在 Stage 1。
- [ ] 禁止任何能执行 shell、修改项目文件、修改游戏资源或写 overlay 的工具出现在 Stage 1。

---

## 3. 任务分解

### 任务 1：创建 MCP 项目骨架

**文件：**
- 创建：`src/PoeStudio.Mcp/PoeStudio.Mcp.csproj`
- 创建：`src/PoeStudio.Mcp/Program.cs`
- 修改：`PoeStudio.sln`
- 修改：`tests/PoeStudio.Tests/PoeStudio.Tests.csproj`

- [x] **步骤 1：运行影响分析**
  由于本任务新增项目，不修改现有函数、类、方法。记录：`Impact: new project only; no existing symbol edited in this task`。

- [x] **步骤 2：创建项目文件**  
  `src/PoeStudio.Mcp/PoeStudio.Mcp.csproj` 内容必须为：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\PoeStudio.Contracts\PoeStudio.Contracts.csproj" />
    <ProjectReference Include="..\PoeStudio.Core\PoeStudio.Core.csproj" />
    <ProjectReference Include="..\PoeStudio.Storage\PoeStudio.Storage.csproj" />
  </ItemGroup>
</Project>
```

  然后安装官方 MCP C# SDK：

```powershell
dotnet add src\PoeStudio.Mcp\PoeStudio.Mcp.csproj package ModelContextProtocol --prerelease
```

  预期：`src\PoeStudio.Mcp\PoeStudio.Mcp.csproj` 中出现实际版本的 `PackageReference Include="ModelContextProtocol"`。不得在项目文件中保留方括号、占位版本或手写未知版本。

- [x] **步骤 3：加入 solution**  
  运行：

```powershell
dotnet sln PoeStudio.sln add src\PoeStudio.Mcp\PoeStudio.Mcp.csproj
```

  预期：输出包含 `Project 'src\PoeStudio.Mcp\PoeStudio.Mcp.csproj' added to the solution.`

- [x] **步骤 4：测试项目引用 MCP 项目**  
  在 `tests/PoeStudio.Tests/PoeStudio.Tests.csproj` 增加：

```xml
<ProjectReference Include="..\..\src\PoeStudio.Mcp\PoeStudio.Mcp.csproj" />
```

- [x] **步骤 4.5：创建临时入口点以满足 Exe 构建**  
  计划修正原因：`PoeStudio.Mcp.csproj` 是 `OutputType=Exe`，但原计划把 `Program.cs` 放在任务 2，导致任务 1 的 solution build 必然失败。先创建只含 stderr 诊断的最小入口点，任务 2 再替换为真实 MCP stdio server。

- [x] **步骤 5：构建验证**  
  运行：

```powershell
dotnet build PoeStudio.sln --no-restore
```

  预期：构建通过，新增项目参与构建。

- [ ] **步骤 6：Commit**  

```powershell
git add PoeStudio.sln src\PoeStudio.Mcp\PoeStudio.Mcp.csproj tests\PoeStudio.Tests\PoeStudio.Tests.csproj
git commit -m "feat(mcp): add POE Studio MCP project shell"
```

### 任务 2：实现 MCP stdio 最小闭环

**文件：**
- 创建：`src/PoeStudio.Mcp/Program.cs`
- 创建：`tests/PoeStudio.Tests/McpProtocolTests.cs`
- 条件创建：`src/PoeStudio.Mcp/McpProtocol.cs`

- [x] **步骤 1：运行影响分析**
  本任务新增文件，不修改现有函数、类、方法。记录：`Impact: new MCP protocol files only; no existing symbol edited in this task`。

- [x] **步骤 2：确认官方 SDK 可用性**  
  运行：

```powershell
dotnet list src\PoeStudio.Mcp\PoeStudio.Mcp.csproj package
```

  预期：输出包含 `ModelContextProtocol`。如果没有，回到任务 1 重新执行 `dotnet add package`。

- [x] **步骤 3：先写协议测试**  
  `McpProtocolTests.cs` 必须覆盖：
  - `initialize` 返回 protocol version、server info、capabilities.tools。
  - `notifications/initialized` 不返回响应。
  - 未知 method 返回 JSON-RPC error，进程不崩溃。
  - 非法 JSON 返回 error，stderr 有诊断，stdout 不写非 JSON 文本。

  测试命令：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~McpProtocolTests
```

  预期：实现前 FAIL，失败原因指向 MCP server 尚未返回预期 lifecycle/tool 响应。

- [x] **步骤 4：用官方 SDK 实现 stdio server**  
  `Program.cs` 必须使用官方 SDK 创建 MCP server、注册 tools、连接 stdio transport。实现时遵守：
  - stdout 只允许 SDK transport 写 JSON-RPC。
  - 诊断日志写 stderr。
  - 工具注册在 server connect 前完成。
  - 所有 tool handler 捕获业务异常并返回 `isError: true`。

- [x] **步骤 5：manual fallback 条件**  
  记录：SDK 包版本 `ModelContextProtocol 1.3.0`。失败命令：`dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~McpProtocolTests`。失败摘要：官方 SDK stdio server 可通过 `initialize`、`notifications/initialized`、unknown method 测试，但 malformed JSON 输入没有向 stdout 返回本计划要求的 JSON-RPC parse error，测试 `Invalid_json_returns_error_and_diagnostics_stay_off_stdout` 超时。已排除工具注册顺序和 stdout 日志污染：server 已在 connect 前注册 tools，`builder.Logging.ClearProviders()`，stdout 无非 JSON 文本。为满足任务 2 协议错误处理要求，切换 `manual fallback`。
  只有官方 SDK 方案无法通过 `codex mcp add` 或进程级测试，且错误不是参数或用法问题时，才允许创建 `McpProtocol.cs`。fallback 版 `McpProtocol.cs` 必须包含：
  - `McpRequest`
  - `McpResponse`
  - `McpError`
  - `McpContent`
  - `McpProtocol.HandleAsync(...)`
  - `McpProtocol.WriteResponseAsync(...)`

  必须支持：
  - `initialize`
  - `notifications/initialized`
  - `tools/list`
  - `tools/call`
  - unknown method error code `-32601`
  - invalid params error code `-32602`
  - internal error code `-32603`

- [x] **步骤 6：fallback stdio loop 要求**  
  仅 fallback 模式适用：`Program.cs` 必须逐行读取 stdin JSON，逐行写 stdout JSON，异常写 stderr。不得使用 `Console.WriteLine` 输出日志。

- [x] **步骤 7：运行协议测试**  

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~McpProtocolTests
```

  预期：PASS。

- [ ] **步骤 8：Commit**

```powershell
git add src\PoeStudio.Mcp\Program.cs src\PoeStudio.Mcp\McpProtocol.cs tests\PoeStudio.Tests\McpProtocolTests.cs
git commit -m "feat(mcp): implement stdio protocol lifecycle"
```

### 任务 3：实现工具注册和 schema

**文件：**
- 创建：`src/PoeStudio.Mcp/McpToolRegistry.cs`
- 创建：`tests/PoeStudio.Tests/McpToolRegistryTests.cs`

- [x] **步骤 1：运行影响分析**  
  本任务新增文件，不修改现有函数、类、方法。记录：`Impact: new MCP tool registry only; no existing symbol edited in this task`。

- [x] **步骤 2：先写工具注册测试**  
  测试必须验证 `tools/list` 返回以下工具名：

```text
poe_get_workspace
poe_list_profiles
poe_get_profile
poe_get_index_status
poe_search_resources
poe_read_resource
poe_datc64_extract_translatable_cells
```

  每个工具必须有 description 和 inputSchema。未知工具调用必须返回 `isError: true`。

  运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~McpToolRegistryTests
```

  预期：实现前 FAIL。

- [x] **步骤 3：实现工具注册表**  
  `McpToolRegistry` 必须提供：
  - `Register(McpToolDefinition definition, Func<JsonElement, CancellationToken, Task<McpToolResult>> handler)`
  - `ListTools()`
  - `CallToolAsync(string name, JsonElement arguments, CancellationToken cancellationToken)`

  `McpToolDefinition` 必须包含：
  - `Name`
  - `Description`
  - `InputSchema`

  `McpToolResult` 必须包含：
  - `Content`
  - `IsError`

- [x] **步骤 4：接入协议层**  
  `tools/list` 调用 `McpToolRegistry.ListTools()`。`tools/call` 调用 `McpToolRegistry.CallToolAsync(...)`。

- [x] **步骤 5：运行工具注册测试**  

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~McpToolRegistryTests
```

  预期：PASS。

- [x] **步骤 6：Commit**

```powershell
git add src\PoeStudio.Mcp\McpToolRegistry.cs src\PoeStudio.Mcp\McpProtocol.cs tests\PoeStudio.Tests\McpToolRegistryTests.cs
git commit -m "feat(mcp): register POE tool schemas"
```

### 任务 4：实现 workspace 解析

**文件：**
- 创建：`src/PoeStudio.Mcp/PoeWorkspaceResolver.cs`
- 创建：`tests/PoeStudio.Tests/McpWorkspaceResolverTests.cs`
- 修改：`src/PoeStudio.Mcp/Program.cs`

- [x] **步骤 1：运行影响分析**  
  修改 `Program.cs` 前运行：

```powershell
gitnexus_impact target=Program direction=upstream repo=POE-Studio
```

  如果 GitNexus 无法区分新增 MCP `Program` 和 API `Program`，记录 `Impact: Program.cs in new PoeStudio.Mcp project; existing API Program.cs not edited`。
  记录：GitNexus impact 命中 `src/PoeStudio.Api/Program.cs:Program`，risk LOW，direct 0；本任务修改的是新项目 `src/PoeStudio.Mcp/Program.cs`，existing API Program.cs not edited。

- [x] **步骤 2：先写 workspace 解析测试**  
  测试必须覆盖：
  - `--workspace-root C:\Example` 优先。
  - `POE_STUDIO_WORKSPACE_ROOT` 次优先。
  - `%LOCALAPPDATA%\PoeStudio\workspace-settings.json` 可解析。
  - 三者都不存在时返回失败，错误消息包含如何修复。

- [x] **步骤 3：实现解析器**  
  `PoeWorkspaceResolver` 必须暴露：

```csharp
public sealed record PoeWorkspaceResolution(bool Success, string? WorkspaceRoot, string Source, string? Error);
public sealed class PoeWorkspaceResolver
{
    public PoeWorkspaceResolution Resolve(string[] args, IReadOnlyDictionary<string, string?> environment);
}
```

  `workspace-settings.json` 至少支持读取：

```json
{ "workspaceRoot": "C:\\Path\\To\\Workspace" }
```

- [x] **步骤 4：Program 接入解析器**  
  启动时创建 resolver，并把 resolution 传入 `PoeMcpTools`。解析失败不退出进程；工具调用时返回 `isError: true`。

- [x] **步骤 5：运行测试**  

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~McpWorkspaceResolverTests
```

  预期：PASS。

- [ ] **步骤 6：Commit**

```powershell
git add src\PoeStudio.Mcp\Program.cs src\PoeStudio.Mcp\PoeWorkspaceResolver.cs tests\PoeStudio.Tests\McpWorkspaceResolverTests.cs
git commit -m "feat(mcp): resolve POE workspace root"
```

### 任务 5：实现 profile、workspace、index 工具

**文件：**
- 创建：`src/PoeStudio.Mcp/PoeMcpTools.cs`
- 创建：`tests/PoeStudio.Tests/McpPoeToolsTests.cs`

- [x] **步骤 1：运行影响分析**
  本任务新增 `PoeMcpTools`，不修改现有函数、类、方法。记录：`Impact: new MCP tool implementations only; existing stores are consumed through public APIs`。
  - Impact: `PoeMcpTools` is a new Stage 1 symbol; GitNexus has not indexed it yet. `McpToolRegistry` is likewise not present in the current index. Scope remains new MCP tool implementations only; existing stores are consumed through public APIs.

- [x] **步骤 2：先写工具测试**
  测试必须覆盖：
  - `poe_get_workspace` 成功返回 root 和 source。
  - `poe_get_workspace` 在未配置 workspace 时返回 `isError: true`。
  - `poe_list_profiles` 返回数组，即使为空也不是错误。
  - `poe_get_profile` 对不存在 id 返回 `isError: true`。
  - `poe_get_index_status` 对缺失索引返回 `exists: false` 和可操作提示。

- [x] **步骤 3：实现工具**
  `PoeMcpTools.RegisterAll(McpToolRegistry registry, PoeWorkspaceResolution workspace)` 必须注册任务 2 工具清单中的必须工具。profile/index 读取优先复用现有 Storage/Core 类型；如现有类型需要配置路径，必须从 workspace root 推导，不得硬编码用户目录。

- [x] **步骤 4：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~McpPoeToolsTests
```

  预期：PASS。

- [x] **步骤 5：Commit**

```powershell
git add src\PoeStudio.Mcp\PoeMcpTools.cs tests\PoeStudio.Tests\McpPoeToolsTests.cs
git commit -m "feat(mcp): expose workspace profile and index tools"
```

### 任务 6：实现 resource 搜索和读取工具

**文件：**
- 修改：`src/PoeStudio.Mcp/PoeMcpTools.cs`
- 创建：`src/PoeStudio.Mcp/PoeResourceContentReader.cs`
- 修改：`tests/PoeStudio.Tests/McpPoeToolsTests.cs`
- 创建：`tests/PoeStudio.Tests/McpResourceContentReaderTests.cs`

- [x] **步骤 1：运行影响分析**

```powershell
gitnexus_impact target=PoeMcpTools direction=upstream repo=POE-Studio
```

  如果 GitNexus 尚未索引新增文件，记录：`Impact: PoeMcpTools new Stage 1 symbol; no external callers except MCP tests and Program`。
  - Impact: `PoeMcpTools` is not present in the current GitNexus index yet. Treat as new Stage 1 symbol; no external callers except MCP tests and `Program`.

- [x] **步骤 2：先写 resource content reader 测试**
  `McpResourceContentReaderTests.cs` 必须覆盖：
  - physical resource 的 `PhysicalPath` 存在时可读取。
  - `resourcePath` 包含 `..`、绝对路径伪装、不同大小写绕过时返回 `isError: true`。
  - native Bundles2 resource 在未接入安全 native 读取服务时返回 `isError: true`，错误码为 `native_resource_not_supported_in_stage1`。
  - `maxBytes` 超过 1048576 返回参数错误。

- [x] **步骤 3：先写 resource 工具测试**
  测试必须覆盖：
  - `poe_search_resources` 按 query 返回最多 `limit` 条。
  - `limit` 大于 100 时返回参数错误。
  - `poe_read_resource` 路径不存在时返回 `isError: true`。
  - `poe_read_resource` 读取文本资源返回 text。
  - `poe_read_resource` 读取二进制资源返回 hex/base64 摘要和 truncated 标记。
  - `poe_read_resource` 遇到 native Bundles2 resource 时不伪造内容。

- [x] **步骤 4：实现只读资源读取边界**
  `PoeResourceContentReader` 必须：
  - 通过 `ResourceIndexStore.GetByPathAsync(profileId, resourcePath, cancellationToken)` 找资源。
  - 对 `resource.PhysicalPath` 做 `Path.GetFullPath`。
  - 仅允许读取存在的 physical path。
  - 如果资源是 native Bundles2 资源或 `PhysicalPath` 为空，返回 `native_resource_not_supported_in_stage1`。
  - 不调用 overlay store，不读 overlay 文件，不扫描磁盘。

- [x] **步骤 5：实现搜索和读取工具**
  搜索必须基于现有 `ResourceIndexStore.SearchAsync`，不得递归扫描整个磁盘。读取必须调用 `PoeResourceContentReader`，不得在 `PoeMcpTools` 中直接 `File.ReadAllBytes`。

- [x] **步骤 6：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~McpPoeToolsTests|FullyQualifiedName~McpResourceContentReaderTests"
```

  预期：PASS。

- [x] **步骤 7：Commit**

```powershell
git add src\PoeStudio.Mcp\PoeMcpTools.cs src\PoeStudio.Mcp\PoeResourceContentReader.cs tests\PoeStudio.Tests\McpPoeToolsTests.cs tests\PoeStudio.Tests\McpResourceContentReaderTests.cs
git commit -m "feat(mcp): expose resource search and read tools"
```

### 任务 7：实现 DATC64 可翻译单元提取

**文件：**
- 修改：`src/PoeStudio.Mcp/PoeMcpTools.cs`
- 创建：`tests/PoeStudio.Tests/McpDatc64ToolTests.cs`

- [ ] **步骤 1：运行影响分析**  

```powershell
gitnexus_impact target=TableInspector direction=upstream repo=POE-Studio
```

  记录 TableInspector 的直接调用方和风险等级。若风险为 HIGH 或 CRITICAL，只允许调用现有 public API，不允许修改 `TableInspector`。
  - Impact: GitNexus reports `TableInspector` upstream risk `CRITICAL`, with direct callers in `tests/PoeStudio.Tests/TableInspectorTests.cs` and `src/PoeStudio.Api/Program.cs`. This task only calls existing public `TableInspector.Inspect(...)`; `TableInspector` was not modified.

- [x] **步骤 2：先写 DATC64 测试**
  测试必须用现有 `TableInspectorTests` 中可复用的 DATC64 样例构造数据，验证：
  - 工具返回 `resourcePath`。
  - 返回 `cells` 数组。
  - 每个 cell 至少包含 `rowIndex`、`columnName`、`sourceText`、`locator`。
  - 空表返回空数组不是错误。
  - 非表格资源返回 `isError: true`。

- [x] **步骤 3：实现提取逻辑**
  `poe_datc64_extract_translatable_cells` 必须先调用 `PoeResourceContentReader` 获取 bytes，再调用 `new TableInspector().Inspect(...)`。候选规则：
  - `sourceText` 非空。
  - 跳过纯数字、空白、明显路径或哈希。
  - 保留 row/column/offset 或能定位回表格编辑的 locator。
  - 返回 warnings，说明跳过了哪些不可翻译项。
  - 如果 resource 是 native Bundles2 且 Stage 1 未接入安全 native 读取服务，返回 `isError: true`，错误码为 `native_resource_not_supported_in_stage1`，不得返回空 cells 冒充成功。

- [x] **步骤 4：运行 DATC64 测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~McpDatc64ToolTests
```

  预期：PASS。

- [x] **步骤 5：Commit**

```powershell
git add src\PoeStudio.Mcp\PoeMcpTools.cs tests\PoeStudio.Tests\McpDatc64ToolTests.cs
git commit -m "feat(mcp): extract DATC64 translatable cells"
```

### 任务 8：进程级 MCP smoke test

**文件：**
- 创建：`tests/PoeStudio.Tests/McpProcessSmokeTests.cs`

- [ ] **步骤 1：运行影响分析**  
  本任务新增测试，不修改生产代码。记录：`Impact: tests only`。

- [ ] **步骤 2：写进程级测试**  
  测试启动：

```powershell
dotnet run --project src\PoeStudio.Mcp\PoeStudio.Mcp.csproj -- --workspace-root "<test-workspace>"
```

  通过 stdin 发送：

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}
```

  然后发送：

```json
{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
```

  预期 stdout 两行都是合法 JSON，第二行包含 `poe_datc64_extract_translatable_cells`。

- [ ] **步骤 3：运行 smoke test**  

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~McpProcessSmokeTests
```

  预期：PASS。

- [ ] **步骤 4：Commit**

```powershell
git add tests\PoeStudio.Tests\McpProcessSmokeTests.cs
git commit -m "test(mcp): verify stdio process handshake"
```

### 任务 9：Codex CLI 真实接入验收

**文件：**
- 创建：`docs/superpowers/reports/2026-05-22-poe-mcp-stage1-acceptance.md`

- [ ] **步骤 1：记录 Codex 版本**  
  运行：

```powershell
codex --version
```

  把输出写入验收报告 `Codex version`。

- [ ] **步骤 2：记录 MCP 实现模式**  
  验收报告必须写入以下其中一行：

```text
MCP implementation mode: official ModelContextProtocol C# SDK
```

```text
MCP implementation mode: manual fallback
Fallback reason:
- SDK failure command: 写入实际执行失败的 `dotnet` 或 `codex` 命令
- SDK failure output summary: 写入错误输出摘要
- Reason this is not usage error: 写入已排除参数、注册顺序、stdio 日志污染、包版本安装错误后的结论
```

- [ ] **步骤 3：注册 MCP server**  
  运行：

```powershell
codex mcp add poe-studio -- dotnet run --project src\PoeStudio.Mcp\PoeStudio.Mcp.csproj -- --workspace-root "C:\Users\25147\Documents\AI-xiangmu\POE-Studio"
```

  预期：命令成功。

- [ ] **步骤 4：确认 MCP 配置**  
  运行：

```powershell
codex mcp get poe-studio
codex mcp list
```

  把输出摘要写入验收报告 `MCP command` 和 `MCP list`。

- [ ] **步骤 5：Codex 调用 POE 工具**  
  运行：

```powershell
codex exec --json -C . "使用 POE Studio MCP 工具列出 profile 和索引状态，不要写入任何文件。"
```

  验收报告必须记录 JSON 事件中能证明工具调用发生的片段，包括工具名或 MCP 调用事件摘要。

- [ ] **步骤 6：DATC64 工具样例验收**  
  运行：

```powershell
codex exec --json -C . "使用 POE Studio MCP 工具查找一个 datc64 资源，并提取最多 5 个可翻译单元。不要写入任何文件。"
```

  把资源路径、返回 cell 数量、前 1-3 个 sourceText 摘要写入验收报告。

- [ ] **步骤 7：native resource 边界验收**  
  如果当前索引中存在 native Bundles2 资源，运行：

```powershell
codex exec --json -C . "使用 POE Studio MCP 工具读取一个 native Bundles2 资源。不要写入任何文件。"
```

  验收报告必须记录：工具返回 `native_resource_not_supported_in_stage1`，或返回真实内容且说明复用了哪个只读 native 读取服务。不能出现空内容成功。

- [ ] **步骤 8：证明没有写入 overlay 或资源文件**  
  运行：

```powershell
git status --short
```

  验收报告必须记录：除计划、源码、测试、报告和运行日志外，没有 overlay draft、资源文件或游戏文件变化。

- [ ] **步骤 9：全量测试**  

```powershell
dotnet test PoeStudio.sln --no-restore
```

  预期：全部通过。

- [ ] **步骤 10：写入 Stage 1 状态**  
  验收报告结尾必须写：

```text
Stage 1 status: PASS
```

  如果任一命令失败，必须写：

```text
Stage 1 status: FAIL
Failure:
- Failed command: 写入实际失败命令
- Failure reason: 写入失败原因摘要
- Next fix task id: 写入需要回到的任务编号，例如 `任务 2 步骤 4`
```

- [ ] **步骤 11：Commit**

```powershell
git add docs\superpowers\reports\2026-05-22-poe-mcp-stage1-acceptance.md
git commit -m "docs(mcp): record Stage 1 acceptance evidence"
```

---

## 4. Stage 1 完成判定

- [ ] `dotnet test PoeStudio.sln --no-restore` 通过。
- [ ] `codex mcp list` 显示 `poe-studio`。
- [ ] `codex exec --json` 有真实 MCP 工具调用证据。
- [ ] `poe_datc64_extract_translatable_cells` 返回 DATC64 可翻译单元样例。
- [ ] 验收报告记录 `MCP implementation mode`。
- [ ] physical resource 读取通过；native Bundles2 resource 不伪成功。
- [ ] 验收报告存在并写明 `Stage 1 status: PASS`。
- [ ] `git status --short` 证明没有未计划的业务数据写入。
- [ ] 执行者停止，等待用户批准进入 Stage 2。

---

## 5. 自检记录

- [ ] 没有 UI 任务。
- [ ] 没有 `/api/agent/*` 或 `/api/codex/*` 任务。
- [ ] 没有真实 overlay 写入任务。
- [ ] 没有 ToolBuilder、任意脚本执行、任意文件写入工具。
- [ ] 每个代码任务都有测试命令和预期结果。
- [ ] 每个阶段验收都有可复制命令。
- [ ] 计划没有引用当前 clean baseline 不存在的旧 Agent 文件。
