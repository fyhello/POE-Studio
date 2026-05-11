# 客户端识别与工作区 MVP 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 构建第一阶段 M1：能识别 POE2 客户端目录、检测 Oodle、创建本地工作区和 `ClientProfile`，并通过本地 API 暴露检测、保存、打开与校验能力。

**架构：** 先搭建 .NET 8 后端核心骨架，不引入 LibGGPK3 作为业务层依赖。`Contracts` 定义 DTO 和枚举，`Core` 负责客户端检测与工作区路径规则，`Storage` 负责 JSON 持久化，`Api` 负责 Minimal API。SQLite 和真实 bundle 索引留给 M2，本计划只使用轻量 JSON 存储，让 M1 可独立交付。

**技术栈：** .NET 8、ASP.NET Core Minimal API、xUnit、System.Text.Json、PowerShell。

---

## 文件结构

创建以下文件：

```text
PoeStudio.sln
src/PoeStudio.Contracts/PoeStudio.Contracts.csproj
src/PoeStudio.Contracts/ClientProfileDtos.cs
src/PoeStudio.Contracts/Enums.cs
src/PoeStudio.Contracts/ApiResponse.cs
src/PoeStudio.Core/PoeStudio.Core.csproj
src/PoeStudio.Core/ClientDetection/ClientDetector.cs
src/PoeStudio.Core/ClientDetection/ClientDetectionResult.cs
src/PoeStudio.Core/Oodle/OodleDetector.cs
src/PoeStudio.Core/Workspace/WorkspaceLayout.cs
src/PoeStudio.Storage/PoeStudio.Storage.csproj
src/PoeStudio.Storage/Profiles/ProfileStore.cs
src/PoeStudio.Api/PoeStudio.Api.csproj
src/PoeStudio.Api/Program.cs
tests/PoeStudio.Tests/PoeStudio.Tests.csproj
tests/PoeStudio.Tests/ClientDetectorTests.cs
tests/PoeStudio.Tests/OodleDetectorTests.cs
tests/PoeStudio.Tests/WorkspaceLayoutTests.cs
tests/PoeStudio.Tests/ProfileStoreTests.cs
tests/PoeStudio.Tests/ApiSmokeTests.cs
```

职责说明：

- `PoeStudio.Contracts`：跨 API 边界的 DTO、枚举、响应包装，不放业务逻辑。
- `PoeStudio.Core`：客户端识别、Oodle 检测、工作区路径规则。
- `PoeStudio.Storage`：Profile 的 JSON 文件存储，避免 M1 提前引入 SQLite 复杂度。
- `PoeStudio.Api`：Minimal API，提供 M1 所需接口。
- `PoeStudio.Tests`：单元测试和 API 冒烟测试。

## 任务 1：创建 .NET 解决方案骨架

**文件：**

- 创建：`PoeStudio.sln`
- 创建：`src/PoeStudio.Contracts/PoeStudio.Contracts.csproj`
- 创建：`src/PoeStudio.Core/PoeStudio.Core.csproj`
- 创建：`src/PoeStudio.Storage/PoeStudio.Storage.csproj`
- 创建：`src/PoeStudio.Api/PoeStudio.Api.csproj`
- 创建：`tests/PoeStudio.Tests/PoeStudio.Tests.csproj`

- [ ] **步骤 1：创建解决方案与项目**

运行：

```powershell
dotnet new sln -n PoeStudio
dotnet new classlib -n PoeStudio.Contracts -o src/PoeStudio.Contracts -f net8.0
dotnet new classlib -n PoeStudio.Core -o src/PoeStudio.Core -f net8.0
dotnet new classlib -n PoeStudio.Storage -o src/PoeStudio.Storage -f net8.0
dotnet new web -n PoeStudio.Api -o src/PoeStudio.Api -f net8.0
dotnet new xunit -n PoeStudio.Tests -o tests/PoeStudio.Tests -f net8.0
dotnet sln PoeStudio.sln add src/PoeStudio.Contracts/PoeStudio.Contracts.csproj
dotnet sln PoeStudio.sln add src/PoeStudio.Core/PoeStudio.Core.csproj
dotnet sln PoeStudio.sln add src/PoeStudio.Storage/PoeStudio.Storage.csproj
dotnet sln PoeStudio.sln add src/PoeStudio.Api/PoeStudio.Api.csproj
dotnet sln PoeStudio.sln add tests/PoeStudio.Tests/PoeStudio.Tests.csproj
```

预期：所有命令成功，根目录出现 `PoeStudio.sln`。

- [ ] **步骤 2：添加项目引用**

运行：

```powershell
dotnet add src/PoeStudio.Core/PoeStudio.Core.csproj reference src/PoeStudio.Contracts/PoeStudio.Contracts.csproj
dotnet add src/PoeStudio.Storage/PoeStudio.Storage.csproj reference src/PoeStudio.Contracts/PoeStudio.Contracts.csproj
dotnet add src/PoeStudio.Api/PoeStudio.Api.csproj reference src/PoeStudio.Contracts/PoeStudio.Contracts.csproj
dotnet add src/PoeStudio.Api/PoeStudio.Api.csproj reference src/PoeStudio.Core/PoeStudio.Core.csproj
dotnet add src/PoeStudio.Api/PoeStudio.Api.csproj reference src/PoeStudio.Storage/PoeStudio.Storage.csproj
dotnet add tests/PoeStudio.Tests/PoeStudio.Tests.csproj reference src/PoeStudio.Contracts/PoeStudio.Contracts.csproj
dotnet add tests/PoeStudio.Tests/PoeStudio.Tests.csproj reference src/PoeStudio.Core/PoeStudio.Core.csproj
dotnet add tests/PoeStudio.Tests/PoeStudio.Tests.csproj reference src/PoeStudio.Storage/PoeStudio.Storage.csproj
dotnet add tests/PoeStudio.Tests/PoeStudio.Tests.csproj reference src/PoeStudio.Api/PoeStudio.Api.csproj
```

预期：引用添加成功。

- [ ] **步骤 3：删除模板占位文件**

删除：

```text
src/PoeStudio.Contracts/Class1.cs
src/PoeStudio.Core/Class1.cs
src/PoeStudio.Storage/Class1.cs
tests/PoeStudio.Tests/UnitTest1.cs
```

运行：

```powershell
Remove-Item -LiteralPath 'src/PoeStudio.Contracts/Class1.cs'
Remove-Item -LiteralPath 'src/PoeStudio.Core/Class1.cs'
Remove-Item -LiteralPath 'src/PoeStudio.Storage/Class1.cs'
Remove-Item -LiteralPath 'tests/PoeStudio.Tests/UnitTest1.cs'
```

预期：文件被删除。

- [ ] **步骤 4：运行空项目构建**

运行：

```powershell
dotnet build PoeStudio.sln
```

预期：Build succeeded。

- [ ] **步骤 5：Commit**

运行：

```powershell
git add PoeStudio.sln src tests
git commit -m "chore: scaffold poe studio solution"
```

## 任务 2：定义 M1 合同类型

**文件：**

- 创建：`src/PoeStudio.Contracts/Enums.cs`
- 创建：`src/PoeStudio.Contracts/ClientProfileDtos.cs`
- 创建：`src/PoeStudio.Contracts/ApiResponse.cs`
- 测试：`tests/PoeStudio.Tests/ClientDetectorTests.cs`

- [ ] **步骤 1：编写枚举**

创建 `src/PoeStudio.Contracts/Enums.cs`：

```csharp
namespace PoeStudio.Contracts;

public enum ClientPlatform
{
    Unknown = 0,
    Official = 1,
    Epic = 2,
    Steam = 3,
    WeGame = 4,
    Custom = 5
}

public enum ClientEntryKind
{
    Unknown = 0,
    Ggpk = 1,
    Bundles2 = 2
}

public enum OodleStatus
{
    Unknown = 0,
    Found = 1,
    Missing = 2
}
```

- [ ] **步骤 2：编写 Profile DTO**

创建 `src/PoeStudio.Contracts/ClientProfileDtos.cs`：

```csharp
namespace PoeStudio.Contracts;

public sealed record ClientProfileDto(
    string Id,
    string DisplayName,
    ClientPlatform Platform,
    ClientEntryKind EntryKind,
    string RootPath,
    string? ContentGgpkPath,
    string? Bundles2Path,
    string? IndexPath,
    OodleStatus OodleStatus,
    string ClientFingerprint,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DetectClientRequest(string RootPath, string? OodleSearchPath = null);

public sealed record DetectClientResponse(
    bool Detected,
    ClientPlatform Platform,
    ClientEntryKind EntryKind,
    string RootPath,
    string? ContentGgpkPath,
    string? Bundles2Path,
    string? IndexPath,
    OodleStatus OodleStatus,
    string? OodlePath,
    string ClientFingerprint,
    IReadOnlyList<string> Warnings);

public sealed record CreateProfileRequest(
    string DisplayName,
    string RootPath,
    ClientPlatform Platform,
    ClientEntryKind EntryKind,
    string? ContentGgpkPath,
    string? Bundles2Path,
    string? IndexPath,
    string ClientFingerprint);
```

- [ ] **步骤 3：编写 API 响应包装**

创建 `src/PoeStudio.Contracts/ApiResponse.cs`：

```csharp
namespace PoeStudio.Contracts;

public sealed record ApiResponse<T>(bool Ok, T? Data, string? ErrorCode, string? Message)
{
    public static ApiResponse<T> Success(T data) => new(true, data, null, null);

    public static ApiResponse<T> Failure(string errorCode, string message) =>
        new(false, default, errorCode, message);
}
```

- [ ] **步骤 4：添加合同类型编译测试**

创建 `tests/PoeStudio.Tests/ClientDetectorTests.cs`：

```csharp
using PoeStudio.Contracts;

namespace PoeStudio.Tests;

public sealed class ClientDetectorTests
{
    [Fact]
    public void ContractTypes_can_be_constructed()
    {
        var response = new DetectClientResponse(
            Detected: true,
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Ggpk,
            RootPath: "C:/Game",
            ContentGgpkPath: "C:/Game/Content.ggpk",
            Bundles2Path: null,
            IndexPath: null,
            OodleStatus: OodleStatus.Missing,
            OodlePath: null,
            ClientFingerprint: "abc",
            Warnings: Array.Empty<string>());

        Assert.True(response.Detected);
        Assert.Equal(ClientEntryKind.Ggpk, response.EntryKind);
    }
}
```

- [ ] **步骤 5：运行测试**

运行：

```powershell
dotnet test PoeStudio.sln --filter ContractTypes_can_be_constructed
```

预期：测试通过。

- [ ] **步骤 6：Commit**

运行：

```powershell
git add src/PoeStudio.Contracts tests/PoeStudio.Tests/ClientDetectorTests.cs
git commit -m "feat: define client profile contracts"
```

## 任务 3：实现 Oodle 检测

**文件：**

- 创建：`src/PoeStudio.Core/Oodle/OodleDetector.cs`
- 创建：`tests/PoeStudio.Tests/OodleDetectorTests.cs`

- [ ] **步骤 1：编写失败测试**

创建 `tests/PoeStudio.Tests/OodleDetectorTests.cs`：

```csharp
using PoeStudio.Contracts;
using PoeStudio.Core.Oodle;

namespace PoeStudio.Tests;

public sealed class OodleDetectorTests
{
    [Fact]
    public void Detect_returns_missing_when_no_candidate_exists()
    {
        var root = CreateTempDirectory();

        var result = OodleDetector.Detect(root);

        Assert.Equal(OodleStatus.Missing, result.Status);
        Assert.Null(result.Path);
    }

    [Fact]
    public void Detect_finds_oo2core_in_root_directory()
    {
        var root = CreateTempDirectory();
        var dll = Path.Combine(root, "oo2core.dll");
        File.WriteAllText(dll, "fake");

        var result = OodleDetector.Detect(root);

        Assert.Equal(OodleStatus.Found, result.Status);
        Assert.Equal(dll, result.Path);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "poe-studio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```powershell
dotnet test PoeStudio.sln --filter OodleDetectorTests
```

预期：编译失败，提示找不到 `PoeStudio.Core.Oodle.OodleDetector`。

- [ ] **步骤 3：实现 OodleDetector**

创建 `src/PoeStudio.Core/Oodle/OodleDetector.cs`：

```csharp
using PoeStudio.Contracts;

namespace PoeStudio.Core.Oodle;

public sealed record OodleDetectionResult(OodleStatus Status, string? Path);

public static class OodleDetector
{
    private static readonly string[] CandidateNames =
    [
        "oo2core.dll",
        "oo2core_9_win64.dll",
        "oo2core_8_win64.dll"
    ];

    public static OodleDetectionResult Detect(string rootPath, string? explicitSearchPath = null)
    {
        foreach (var dir in BuildSearchDirectories(rootPath, explicitSearchPath))
        {
            foreach (var name in CandidateNames)
            {
                var candidate = System.IO.Path.Combine(dir, name);
                if (File.Exists(candidate))
                {
                    return new OodleDetectionResult(OodleStatus.Found, candidate);
                }
            }
        }

        return new OodleDetectionResult(OodleStatus.Missing, null);
    }

    private static IEnumerable<string> BuildSearchDirectories(string rootPath, string? explicitSearchPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitSearchPath))
        {
            yield return explicitSearchPath;
        }

        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            yield return rootPath;
            yield return System.IO.Path.Combine(rootPath, "Bundles2");
            yield return System.IO.Path.Combine(rootPath, "Bundlebak");
        }
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：

```powershell
dotnet test PoeStudio.sln --filter OodleDetectorTests
```

预期：2 个测试通过。

- [ ] **步骤 5：Commit**

运行：

```powershell
git add src/PoeStudio.Core/Oodle tests/PoeStudio.Tests/OodleDetectorTests.cs
git commit -m "feat: add oodle runtime detection"
```

## 任务 4：实现客户端目录识别

**文件：**

- 创建：`src/PoeStudio.Core/ClientDetection/ClientDetectionResult.cs`
- 创建：`src/PoeStudio.Core/ClientDetection/ClientDetector.cs`
- 修改：`tests/PoeStudio.Tests/ClientDetectorTests.cs`

- [ ] **步骤 1：替换客户端检测测试**

将 `tests/PoeStudio.Tests/ClientDetectorTests.cs` 替换为：

```csharp
using PoeStudio.Contracts;
using PoeStudio.Core.ClientDetection;

namespace PoeStudio.Tests;

public sealed class ClientDetectorTests
{
    [Fact]
    public void Detect_identifies_official_client_by_content_ggpk()
    {
        var root = CreateTempDirectory();
        File.WriteAllBytes(Path.Combine(root, "Content.ggpk"), [1, 2, 3]);

        var result = ClientDetector.Detect(root);

        Assert.True(result.Detected);
        Assert.Equal(ClientPlatform.Official, result.Platform);
        Assert.Equal(ClientEntryKind.Ggpk, result.EntryKind);
        Assert.EndsWith("Content.ggpk", result.ContentGgpkPath);
    }

    [Fact]
    public void Detect_identifies_bundles_client_by_index_file()
    {
        var root = CreateTempDirectory();
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
        File.WriteAllBytes(Path.Combine(bundles, "_.index.bin"), [1, 2, 3]);

        var result = ClientDetector.Detect(root);

        Assert.True(result.Detected);
        Assert.Equal(ClientEntryKind.Bundles2, result.EntryKind);
        Assert.EndsWith("_.index.bin", result.IndexPath);
    }

    [Fact]
    public void Detect_returns_warning_when_root_is_not_client()
    {
        var root = CreateTempDirectory();

        var result = ClientDetector.Detect(root);

        Assert.False(result.Detected);
        Assert.Contains(result.Warnings, item => item.Contains("未找到"));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "poe-studio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```powershell
dotnet test PoeStudio.sln --filter ClientDetectorTests
```

预期：编译失败，提示找不到 `ClientDetector`。

- [ ] **步骤 3：实现检测结果类型**

创建 `src/PoeStudio.Core/ClientDetection/ClientDetectionResult.cs`：

```csharp
using PoeStudio.Contracts;

namespace PoeStudio.Core.ClientDetection;

public sealed record ClientDetectionResult(
    bool Detected,
    ClientPlatform Platform,
    ClientEntryKind EntryKind,
    string RootPath,
    string? ContentGgpkPath,
    string? Bundles2Path,
    string? IndexPath,
    OodleStatus OodleStatus,
    string? OodlePath,
    string ClientFingerprint,
    IReadOnlyList<string> Warnings);
```

- [ ] **步骤 4：实现 ClientDetector**

创建 `src/PoeStudio.Core/ClientDetection/ClientDetector.cs`：

```csharp
using System.Security.Cryptography;
using System.Text;
using PoeStudio.Contracts;
using PoeStudio.Core.Oodle;

namespace PoeStudio.Core.ClientDetection;

public static class ClientDetector
{
    public static ClientDetectionResult Detect(string rootPath, string? oodleSearchPath = null)
    {
        var warnings = new List<string>();
        var fullRoot = Path.GetFullPath(rootPath);
        var contentGgpk = Path.Combine(fullRoot, "Content.ggpk");
        var bundles2 = Path.Combine(fullRoot, "Bundles2");
        var index = Path.Combine(bundles2, "_.index.bin");
        var oodle = OodleDetector.Detect(fullRoot, oodleSearchPath);

        if (File.Exists(contentGgpk))
        {
            return BuildResult(
                detected: true,
                platform: DetectPlatform(fullRoot),
                entryKind: ClientEntryKind.Ggpk,
                rootPath: fullRoot,
                contentGgpkPath: contentGgpk,
                bundles2Path: Directory.Exists(bundles2) ? bundles2 : null,
                indexPath: File.Exists(index) ? index : null,
                oodle,
                warnings);
        }

        if (File.Exists(index))
        {
            return BuildResult(
                detected: true,
                platform: DetectPlatform(fullRoot),
                entryKind: ClientEntryKind.Bundles2,
                rootPath: fullRoot,
                contentGgpkPath: null,
                bundles2Path: bundles2,
                indexPath: index,
                oodle,
                warnings);
        }

        warnings.Add("未找到 Content.ggpk 或 Bundles2/_.index.bin。");
        return BuildResult(
            detected: false,
            platform: ClientPlatform.Unknown,
            entryKind: ClientEntryKind.Unknown,
            rootPath: fullRoot,
            contentGgpkPath: null,
            bundles2Path: Directory.Exists(bundles2) ? bundles2 : null,
            indexPath: null,
            oodle,
            warnings);
    }

    private static ClientDetectionResult BuildResult(
        bool detected,
        ClientPlatform platform,
        ClientEntryKind entryKind,
        string rootPath,
        string? contentGgpkPath,
        string? bundles2Path,
        string? indexPath,
        OodleDetectionResult oodle,
        IReadOnlyList<string> warnings)
    {
        var fingerprint = BuildFingerprint(rootPath, contentGgpkPath, indexPath);
        return new ClientDetectionResult(
            detected,
            platform,
            entryKind,
            rootPath,
            contentGgpkPath,
            bundles2Path,
            indexPath,
            oodle.Status,
            oodle.Path,
            fingerprint,
            warnings);
    }

    private static ClientPlatform DetectPlatform(string rootPath)
    {
        if (rootPath.Contains("WeGame", StringComparison.OrdinalIgnoreCase) ||
            rootPath.Contains("rail_apps", StringComparison.OrdinalIgnoreCase))
        {
            return ClientPlatform.WeGame;
        }

        return ClientPlatform.Official;
    }

    private static string BuildFingerprint(string rootPath, string? contentGgpkPath, string? indexPath)
    {
        var builder = new StringBuilder();
        builder.Append(rootPath);
        AppendFileSignature(builder, contentGgpkPath);
        AppendFileSignature(builder, indexPath);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..16].ToLowerInvariant();
    }

    private static void AppendFileSignature(StringBuilder builder, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            builder.Append("|missing");
            return;
        }

        var info = new FileInfo(path);
        builder.Append('|').Append(info.FullName).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks);
    }
}
```

- [ ] **步骤 5：运行测试验证通过**

运行：

```powershell
dotnet test PoeStudio.sln --filter ClientDetectorTests
```

预期：3 个测试通过。

- [ ] **步骤 6：Commit**

运行：

```powershell
git add src/PoeStudio.Core/ClientDetection tests/PoeStudio.Tests/ClientDetectorTests.cs
git commit -m "feat: detect poe client layouts"
```

## 任务 5：实现工作区路径规则

**文件：**

- 创建：`src/PoeStudio.Core/Workspace/WorkspaceLayout.cs`
- 创建：`tests/PoeStudio.Tests/WorkspaceLayoutTests.cs`

- [ ] **步骤 1：编写失败测试**

创建 `tests/PoeStudio.Tests/WorkspaceLayoutTests.cs`：

```csharp
using PoeStudio.Core.Workspace;

namespace PoeStudio.Tests;

public sealed class WorkspaceLayoutTests
{
    [Fact]
    public void ForProfile_returns_stable_profile_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-tests", Guid.NewGuid().ToString("N"));

        var layout = WorkspaceLayout.ForProfile(root, "profile-1");

        Assert.Equal(Path.Combine(root, "profiles", "profile-1"), layout.ProfileRoot);
        Assert.Equal(Path.Combine(layout.ProfileRoot, "profile.json"), layout.ProfileJsonPath);
        Assert.Equal(Path.Combine(layout.ProfileRoot, "cache"), layout.CacheRoot);
        Assert.Equal(Path.Combine(layout.ProfileRoot, "overlay"), layout.OverlayRoot);
        Assert.Equal(Path.Combine(layout.ProfileRoot, "builds"), layout.BuildsRoot);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```powershell
dotnet test PoeStudio.sln --filter WorkspaceLayoutTests
```

预期：编译失败，提示找不到 `WorkspaceLayout`。

- [ ] **步骤 3：实现 WorkspaceLayout**

创建 `src/PoeStudio.Core/Workspace/WorkspaceLayout.cs`：

```csharp
namespace PoeStudio.Core.Workspace;

public sealed record WorkspaceLayout(
    string WorkspaceRoot,
    string ProfileId,
    string ProfileRoot,
    string ProfileJsonPath,
    string CacheRoot,
    string RawCacheRoot,
    string PreviewCacheRoot,
    string OverlayRoot,
    string OverlayFilesRoot,
    string BuildsRoot,
    string AuditRoot)
{
    public static WorkspaceLayout ForProfile(string workspaceRoot, string profileId)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var profileRoot = Path.Combine(root, "profiles", profileId);
        return new WorkspaceLayout(
            WorkspaceRoot: root,
            ProfileId: profileId,
            ProfileRoot: profileRoot,
            ProfileJsonPath: Path.Combine(profileRoot, "profile.json"),
            CacheRoot: Path.Combine(profileRoot, "cache"),
            RawCacheRoot: Path.Combine(profileRoot, "cache", "raw"),
            PreviewCacheRoot: Path.Combine(profileRoot, "cache", "preview"),
            OverlayRoot: Path.Combine(profileRoot, "overlay"),
            OverlayFilesRoot: Path.Combine(profileRoot, "overlay", "files"),
            BuildsRoot: Path.Combine(profileRoot, "builds"),
            AuditRoot: Path.Combine(profileRoot, "audit"));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ProfileRoot);
        Directory.CreateDirectory(CacheRoot);
        Directory.CreateDirectory(RawCacheRoot);
        Directory.CreateDirectory(PreviewCacheRoot);
        Directory.CreateDirectory(OverlayRoot);
        Directory.CreateDirectory(OverlayFilesRoot);
        Directory.CreateDirectory(BuildsRoot);
        Directory.CreateDirectory(AuditRoot);
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：

```powershell
dotnet test PoeStudio.sln --filter WorkspaceLayoutTests
```

预期：1 个测试通过。

- [ ] **步骤 5：Commit**

运行：

```powershell
git add src/PoeStudio.Core/Workspace tests/PoeStudio.Tests/WorkspaceLayoutTests.cs
git commit -m "feat: define workspace layout"
```

## 任务 6：实现 Profile JSON 存储

**文件：**

- 创建：`src/PoeStudio.Storage/Profiles/ProfileStore.cs`
- 创建：`tests/PoeStudio.Tests/ProfileStoreTests.cs`

- [ ] **步骤 1：编写失败测试**

创建 `tests/PoeStudio.Tests/ProfileStoreTests.cs`：

```csharp
using PoeStudio.Contracts;
using PoeStudio.Storage.Profiles;

namespace PoeStudio.Tests;

public sealed class ProfileStoreTests
{
    [Fact]
    public async Task Save_and_list_profiles_roundtrips_json()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "poe-studio-tests", Guid.NewGuid().ToString("N"));
        var store = new ProfileStore(workspace);
        var profile = new ClientProfileDto(
            Id: "profile-1",
            DisplayName: "Official",
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Ggpk,
            RootPath: "C:/Game",
            ContentGgpkPath: "C:/Game/Content.ggpk",
            Bundles2Path: null,
            IndexPath: null,
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "abc",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        await store.SaveAsync(profile, CancellationToken.None);
        var items = await store.ListAsync(CancellationToken.None);

        Assert.Single(items);
        Assert.Equal("profile-1", items[0].Id);
        Assert.Equal("Official", items[0].DisplayName);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```powershell
dotnet test PoeStudio.sln --filter ProfileStoreTests
```

预期：编译失败，提示找不到 `ProfileStore`。

- [ ] **步骤 3：实现 ProfileStore**

创建 `src/PoeStudio.Storage/Profiles/ProfileStore.cs`：

```csharp
using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Storage.Profiles;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string workspaceRoot;

    public ProfileStore(string workspaceRoot)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public async Task SaveAsync(ClientProfileDto profile, CancellationToken cancellationToken)
    {
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, profile.Id);
        layout.EnsureDirectories();
        await using var stream = File.Create(layout.ProfileJsonPath);
        await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<ClientProfileDto>> ListAsync(CancellationToken cancellationToken)
    {
        var profilesRoot = Path.Combine(workspaceRoot, "profiles");
        if (!Directory.Exists(profilesRoot))
        {
            return Array.Empty<ClientProfileDto>();
        }

        var items = new List<ClientProfileDto>();
        foreach (var file in Directory.EnumerateFiles(profilesRoot, "profile.json", SearchOption.AllDirectories))
        {
            await using var stream = File.OpenRead(file);
            var profile = await JsonSerializer.DeserializeAsync<ClientProfileDto>(stream, JsonOptions, cancellationToken);
            if (profile is not null)
            {
                items.Add(profile);
            }
        }

        return items.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<ClientProfileDto?> GetAsync(string profileId, CancellationToken cancellationToken)
    {
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, profileId);
        if (!File.Exists(layout.ProfileJsonPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(layout.ProfileJsonPath);
        return await JsonSerializer.DeserializeAsync<ClientProfileDto>(stream, JsonOptions, cancellationToken);
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：

```powershell
dotnet test PoeStudio.sln --filter ProfileStoreTests
```

预期：1 个测试通过。

- [ ] **步骤 5：Commit**

运行：

```powershell
git add src/PoeStudio.Storage/Profiles tests/PoeStudio.Tests/ProfileStoreTests.cs
git commit -m "feat: persist client profiles"
```

## 任务 7：实现 M1 Minimal API

**文件：**

- 修改：`src/PoeStudio.Api/Program.cs`
- 创建：`tests/PoeStudio.Tests/ApiSmokeTests.cs`
- 修改：`tests/PoeStudio.Tests/PoeStudio.Tests.csproj`

- [ ] **步骤 1：添加测试包**

运行：

```powershell
dotnet add tests/PoeStudio.Tests/PoeStudio.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing --version 8.0.20
```

预期：包安装成功。

- [ ] **步骤 2：编写 API 冒烟测试**

创建 `tests/PoeStudio.Tests/ApiSmokeTests.cs`：

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PoeStudio.Contracts;

namespace PoeStudio.Tests;

public sealed class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ApiSmokeTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PoeStudio:WorkspaceRoot", Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N")));
        });
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Detect_returns_bundles_layout()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "_.index.bin"), [1, 2, 3]);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/profiles/detect", new DetectClientRequest(root));
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<DetectClientResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload?.Ok);
        Assert.Equal(ClientEntryKind.Bundles2, payload?.Data?.EntryKind);
    }
}
```

- [ ] **步骤 3：运行测试验证失败**

运行：

```powershell
dotnet test PoeStudio.sln --filter ApiSmokeTests
```

预期：编译失败或测试失败，因为 API 尚未实现所需端点。

- [ ] **步骤 4：实现 Program.cs**

将 `src/PoeStudio.Api/Program.cs` 替换为：

```csharp
using PoeStudio.Contracts;
using PoeStudio.Core.ClientDetection;
using PoeStudio.Storage.Profiles;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors();
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var workspaceRoot = config["PoeStudio:WorkspaceRoot"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoeStudio");
    return new ProfileStore(workspaceRoot);
});

var app = builder.Build();

app.MapGet("/api/health", () => ApiResponse<object>.Success(new
{
    status = "ok",
    utcTime = DateTimeOffset.UtcNow
}));

app.MapGet("/api/profiles", async (ProfileStore store, CancellationToken cancellationToken) =>
{
    var profiles = await store.ListAsync(cancellationToken);
    return ApiResponse<IReadOnlyList<ClientProfileDto>>.Success(profiles);
});

app.MapPost("/api/profiles/detect", (DetectClientRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.RootPath))
    {
        return Results.BadRequest(ApiResponse<DetectClientResponse>.Failure("invalid_root_path", "客户端目录不能为空。"));
    }

    var result = ClientDetector.Detect(request.RootPath, request.OodleSearchPath);
    var response = new DetectClientResponse(
        result.Detected,
        result.Platform,
        result.EntryKind,
        result.RootPath,
        result.ContentGgpkPath,
        result.Bundles2Path,
        result.IndexPath,
        result.OodleStatus,
        result.OodlePath,
        result.ClientFingerprint,
        result.Warnings);

    return Results.Ok(ApiResponse<DetectClientResponse>.Success(response));
});

app.MapPost("/api/profiles", async (CreateProfileRequest request, ProfileStore store, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.RootPath))
    {
        return Results.BadRequest(ApiResponse<ClientProfileDto>.Failure("invalid_profile", "名称和客户端目录不能为空。"));
    }

    var now = DateTimeOffset.UtcNow;
    var profile = new ClientProfileDto(
        Id: Guid.NewGuid().ToString("N"),
        DisplayName: request.DisplayName,
        Platform: request.Platform,
        EntryKind: request.EntryKind,
        RootPath: request.RootPath,
        ContentGgpkPath: request.ContentGgpkPath,
        Bundles2Path: request.Bundles2Path,
        IndexPath: request.IndexPath,
        OodleStatus: OodleStatus.Unknown,
        ClientFingerprint: request.ClientFingerprint,
        CreatedAt: now,
        UpdatedAt: now);

    await store.SaveAsync(profile, cancellationToken);
    return Results.Ok(ApiResponse<ClientProfileDto>.Success(profile));
});

app.Run();

public partial class Program
{
}
```

- [ ] **步骤 5：运行 API 测试验证通过**

运行：

```powershell
dotnet test PoeStudio.sln --filter ApiSmokeTests
```

预期：2 个测试通过。

- [ ] **步骤 6：运行全量测试**

运行：

```powershell
dotnet test PoeStudio.sln
```

预期：所有测试通过。

- [ ] **步骤 7：Commit**

运行：

```powershell
git add src/PoeStudio.Api tests/PoeStudio.Tests
git commit -m "feat: expose client profile api"
```

## 任务 8：手工验证真实客户端检测

**文件：**

- 不创建源文件。
- 验证真实路径，不写入客户端目录。

- [ ] **步骤 1：启动 API**

运行：

```powershell
dotnet run --project src/PoeStudio.Api/PoeStudio.Api.csproj --urls http://127.0.0.1:5123
```

预期：控制台显示服务监听 `http://127.0.0.1:5123`。

- [ ] **步骤 2：验证国际服目录检测**

另开 PowerShell 运行：

```powershell
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5123/api/profiles/detect' -ContentType 'application/json' -Body (@{
  rootPath = 'E:\PSAutoRecover\ui\rood\Grinding Gear Games\Path of Exile 2'
} | ConvertTo-Json)
```

预期：

- `ok` 为 `true`。
- `data.detected` 为 `true`。
- `data.entryKind` 为 `Ggpk`。
- `data.contentGgpkPath` 指向 `Content.ggpk`。

- [ ] **步骤 3：验证国服目录检测**

运行：

```powershell
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5123/api/profiles/detect' -ContentType 'application/json' -Body (@{
  rootPath = 'C:\WeGameApps\rail_apps\流放之路：降临(2002052)'
} | ConvertTo-Json)
```

预期：

- `ok` 为 `true`。
- `data.detected` 为 `true`。
- `data.entryKind` 为 `Bundles2`。
- `data.indexPath` 指向 `Bundles2\_.index.bin`。
- `data.platform` 为 `WeGame`。

- [ ] **步骤 4：停止 API**

在服务控制台按 `Ctrl+C`。

预期：服务正常退出。

- [ ] **步骤 5：Commit 验证说明**

如果手工验证发现需要补充 README，创建 `docs/m1-verification.md` 记录命令与结果，然后运行：

```powershell
git add docs/m1-verification.md
git commit -m "docs: record m1 verification"
```

如果没有新增文档，此步骤不提交。

## 计划自检

- 规格覆盖：本计划覆盖设计规格中的 M1「客户端识别与项目工作区」，不覆盖 M2 到 M5。
- 范围控制：没有实现 SQLite、资源索引、预览、overlay 编辑或补丁构建。
- 类型一致性：`ClientPlatform`、`ClientEntryKind`、`OodleStatus`、`ClientProfileDto`、`DetectClientResponse` 在任务 2 定义，并在后续任务复用。
- 测试节奏：每个核心模块先有失败测试，再实现，再运行验证。
- 提交节奏：每个任务完成后独立 commit。
