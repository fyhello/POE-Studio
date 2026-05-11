# Bundles2 Writer 边界说明

## 背景

M5 已经完成补丁构建工作流，但当前 `OverlayBundleMvp` 只负责验证 dry-run、manifest、rollback、zip 模板和双文件输出骨架，不声称生成可直接被游戏加载的真实 Bundles2 补丁。

为了防止 UI、脚本或后续自动化误用占位包，构建请求现在显式区分写入器：

- `Mvp`：当前可用，输出审计型 MVP 包。
- `NativeBundles2`：预留给自研内核，未接入时明确返回 `native_writer_unavailable`。
- `LibGgpk3Adapter`：预留给原型/验证适配器，未接入时明确返回 `libggpk3_writer_unavailable`。

## LibGGPK3 调研摘记

本地 `LibGGPK3-main` 中真实 Bundles2 关键链路：

- `LibBundle3.Index` 负责读取 `Bundles2/_.index.bin`。
- `Index` 中记录 bundle 表、file 表、directory 表和路径解析数据。
- `LibBundle3.Records.FileRecord.Write` 会把新内容写入目标 bundle，并更新 `BundleRecord`、`Offset`、`Size`。
- `FileRecord.Redirect` 是底层改向文件到新 bundle/offset/size 的关键动作。
- `Index.Save` 负责保存修改后的 index。
- 示例 `Examples/PatchBundle3/Program.cs` 使用 `new LibBundle3.Index(path, false)` 和 `LibBundle3.Index.Replace(index, zip.Entries, ...)` 完成 zip 覆盖。

## 当前工程边界

`PatchBuildService` 只处理：

- dry-run 结果；
- 输出目录和平台模板；
- manifest；
- rollback manifest；
- zip 打包。

真实包写入由 `IPatchPackageWriter` 负责。后续实现只需要新增 writer：

- `NativeBundles2PackageWriter`：自研读取/写入 `_.index.bin` 和 patch bundle。
- `LibGgpk3PackageWriter`：仅用于原型验证或取得授权后的适配器。

## 下一步

推荐优先做 `NativeBundles2IndexReader`：

1. 只读解析 index header、bundle records、file records 数量。
2. 能在真实国服 `_.index.bin` 上快速输出统计和警告。
3. 不修改客户端文件，所有解析结果写入 workspace cache。
4. 通过行为对照测试再推进 writer。

## 真实国服 index 头部探测

只读探测路径：

```text
C:\WeGameApps\rail_apps\流放之路：降临(2002052)\Bundles2\_.index.bin
```

观测结果：

- 文件大小：124,951,295 bytes。
- 解压后大小：153,151,305 bytes。
- 压缩后大小：124,948,895 bytes。
- Header size：2,388 bytes。
- Compressor：12。
- Chunk count：585。
- Chunk size：262,144 bytes。
- First compressed chunk：34,636 bytes。

结论：当前 Native 探针能安全读取 bundle header；内部 index 记录解析需要 Oodle 解压支持后再推进。
