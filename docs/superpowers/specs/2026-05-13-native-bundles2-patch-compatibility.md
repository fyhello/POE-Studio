# Native Bundles2 补丁兼容性复盘与固定方案

## 背景

2026-05-13 生成的国际服双文件补丁在工具内验证通过，但实机加载失败。对比可用补丁后确认，问题不在 zip 目录结构，也不在 `Bundles2/_.index.bin` 与 `Bundles2/Tiny.V0.1.bundle.bin` 这两个固定文件名本身，而在 Native bundle header 与压缩器标识不兼容。

可用参考补丁来自：

- `C:\Users\25147\Documents\AI-xiangmu\0.4.0j(4.4.0.12)\Bundles2\poe2_patch_studio`
- `E:\poe2_h_work\studio_custom_package_20260513152555\adapted_patch.zip`

修复后的本项目补丁示例：

- `C:\Users\25147\AppData\Local\PoeStudio\profiles\75c5bef9860a45658cbb2a41aae5c057\builds\20260513154448-official-patch.zip`

## 表现

旧补丁可以正常生成，工具内 `PatchPackageVerifier` 也能解压并解析，但客户端或外部注入工具加载后不可用。

这说明“自研解析器能读通”不等于“游戏客户端兼容”。后续判断 Native Bundles2 补丁是否正确，必须同时参考游戏兼容格式、LibBundle3 行为和实机结果。

## 根因

旧实现把 `_.index.bin` 和 `Tiny.V0.1.bundle.bin` 都用同一个 Oodle compressor id 写出，并且 bundle header 第 16 字节位置的 unknown/flag 字段保持为 `0`。

对比结果如下：

| 文件 | 可用补丁 compressor | 可用补丁 flag | 旧补丁 compressor | 旧补丁 flag |
| --- | ---: | ---: | ---: | ---: |
| `Bundles2/_.index.bin` | `8` | `1` | `13` | `0` |
| `Bundles2/Tiny.V0.1.bundle.bin` | `9` | `1` | `13` | `0` |

LibBundle3 枚举含义：

| compressor id | 名称 |
| ---: | --- |
| `8` | `Kraken` |
| `9` | `Mermaid` |
| `13` | `Leviathan` |

因此根因是：我们手写 Native bundle header 和压缩策略时，没有完全复刻 LibBundle3/客户端实际接受的格式。

## 固定方案

Native Bundles2 双文件补丁必须固定以下规则：

| 输出文件 | 固定路径 | compressor id | header flag |
| --- | --- | ---: | ---: |
| 索引 | `Bundles2/_.index.bin` | `8` (`Kraken`) | `1` |
| 资源包 | `Bundles2/Tiny.V0.1.bundle.bin` | `9` (`Mermaid`) | `1` |

其他固定要求：

- 不允许用改 bundle 文件名规避问题。目标补丁文件名必须是 `_.index.bin` 和 `Tiny.V0.1.bundle.bin`。
- 复用 `Tiny.V0.1.bundle.bin` 时，必须先保留原始 Tiny 解压内容，再把 overlay 数据追加到原 payload 尾部。
- `_.index.bin` 中指向 Tiny 的 file record 需要更新 offset 和 size，bundle record 的 uncompressed size 必须等于新 Tiny 解压总长度。
- 压缩接口缺少 `OodleLZ_GetCompressedBufferSize` 时，可以使用公式估算最大压缩缓冲区，但仍必须使用正确 compressor id。
- `__copy__` 只能用于测试，不代表正式补丁兼容游戏。

## 当前代码落点

关键实现位置：

- `src/PoeStudio.Core/Native/OodleCodec.cs`
  - `KrakenCompressorId = 8`
  - `MermaidCompressorId = 9`
  - 支持按调用方选择 compressor id。
- `src/PoeStudio.Core/Native/NativeBundleCompressor.cs`
  - `Compress(..., headerUnknown: 1)` 写入 header flag。
- `src/PoeStudio.Core/Patching/NativeBundles2PackageWriter.cs`
  - payload/Tiny 使用 Mermaid。
  - index 使用 Kraken。
  - 复用 Tiny 时读取原始 bundle payload 并追加 overlay。
- `src/PoeStudio.Core/Patching/NativeIndexBundleWriter.cs`
  - `_.index.bin` 写出时使用 header flag `1`。
- `src/PoeStudio.Core/Patching/NativePayloadBundleWriter.cs`
  - `Tiny.V0.1.bundle.bin` 写出时使用 header flag `1`。

## 回归测试

固定行为由以下测试覆盖：

- `NativeBundleCompressorTests`
  - header compressor 与 flag 写入。
  - `oo2core.dll` 缺少 `OodleLZ_GetCompressedBufferSize` 时仍可创建压缩 codec。
- `NativeIndexBundleWriterTests`
  - index bundle 写出 header flag。
- `NativePayloadBundleWriterTests`
  - payload bundle 写出 header flag。
  - 复用 Tiny 时保留原始 payload 前缀。
- `PatchBuildServiceTests`
  - 复用本地 Bundles2 Tiny。
  - 复用 GGPK 内嵌 Tiny。
- `PatchPackageVerifierTests`
  - index 中 bundle uncompressed size 与实际 Tiny 解压长度必须一致。

推荐验证命令：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj -c Release --filter "PatchBuildServiceTests|NativePayloadBundleWriterTests|NativeIndexRewriteDryRunTests|ResourceIndexStoreTests|PatchPackageVerifierTests|OverlayStoreTests|NativeBundleCompressorTests|NativeIndexBundleWriterTests"
dotnet build src\PoeStudio.Api\PoeStudio.Api.csproj -c Release
```

## 下次排查方向

如果再次出现“补丁能生成，但游戏不可用”，先按下面顺序排查：

1. 确认 zip 里只有目标双文件，路径为 `Bundles2/_.index.bin` 和 `Bundles2/Tiny.V0.1.bundle.bin`。
2. 读取两个文件 header：
   - `_.index.bin` 必须是 `codec=8`、`flag=1`。
   - `Tiny.V0.1.bundle.bin` 必须是 `codec=9`、`flag=1`。
3. 解压 Tiny，确认 index 中 Tiny bundle record 的 uncompressed size 等于实际解压长度。
4. 抽查 overlay 路径对应 file record：
   - bundle index 指向 Tiny。
   - offset 和 size 不越界。
   - 读取出的 bytes 与 overlay 文件 hash 一致。
5. 用 LibBundle3 打开新 `_.index.bin`，不要只依赖本项目自研 parser。
6. 和一份已知可用补丁逐字段对比 header、bundle record、file record 和 Tiny 解压长度。
7. 最后再做实机测试。

## 不能再犯的错误

- 不能把所有 bundle 统一写成同一个 compressor id。
- 不能因为 `PatchPackageVerifier` 通过就认定客户端一定可加载。
- 不能只改 zip 包路径或文件名来规避格式问题。
- 不能在没有可用补丁对比数据时继续猜测式修复。

## 已确认结果

修复后生成的新补丁：

- `Tiny.V0.1.bundle.bin`：`codec=9`、`flag=1`
- `_.index.bin`：`codec=8`、`flag=1`
- `PatchPackageVerifier`：通过
- 用户实机确认：可以使用
