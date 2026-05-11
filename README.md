# POE Studio

POE2 客户端编辑与补丁工具 MVP。

## 快速启动

在 Windows PowerShell 中运行：

```powershell
.\启动_POE_Studio.ps1
```

或者手动启动：

```powershell
dotnet run --project src\PoeStudio.Api\PoeStudio.Api.csproj --urls http://localhost:5087
```

打开：

```text
http://localhost:5087/
```

## 当前主流程

1. 选择“国服路径”或“国际服路径”，确认 `oo2core.dll` 路径。
2. 点击“一键接入”。
3. 点击“建立真实索引”，等待进度条完成。
4. 搜索资源，点击资源预览。
5. 文本资源可保存覆盖，也可按搜索条件批量覆盖。
6. 点击“补丁预检”查看改动，再点击“生成补丁”。
7. 在“最近补丁”中下载 zip。

## 说明

- 当前补丁写入模式是 `OverlayBundleMvp`，用于可审计补丁包输出闭环。
- `oo2core.dll` 由用户本机提供，项目不分发。
- LibGGPK3 仅作为研究参考，不直接链接或复制源码。
