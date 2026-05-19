# 构建与恢复说明

这份说明用于把仓库恢复到可开发状态，适合新机器、重装系统、重新拉取代码后的第一次搭建。

## 当前前置条件

- Visual Studio 2022
- Windows x64
- .NET Framework Developer Pack 4.8
- 本地 NuGet 源 `third_party\nuget`
- 本地程序集目录 `packages\`、`third_party\mes\`、`third_party\faratronic-auth\`、`BusbarCompressionSystem\dll\`

## 关键依赖位置

- 主工程：`BusbarCompressionSystem\BusbarCompressionSystem.csproj`
- 相机工程：`Camera\Camera.csproj`
- 海康相机托管封装：`BusbarCompressionSystem\dll\MvCameraControl.Net.dll`
- 视觉与图像库：`BusbarCompressionSystem\dll\halcon.dll`、`BusbarCompressionSystem\dll\halcondotnet.dll`
- MES 本地程序集：`third_party\mes\GetSNByMP.dll`、`third_party\mes\Oracle.ManagedDataAccess.dll`
- 认证本地程序集：`third_party\faratronic-auth\net45\`、`third_party\faratronic-auth\net462\`

## 离线包状态

仓库当前提供的 MESAutoLineClient 离线包：

- `third_party\nuget\MESAutoLineClient.1.0.86.15454.nupkg`
- `third_party\nuget\MESAutoLineClient.1.0.129.20464.nupkg`
- `third_party\nuget\MESAutoLineClient.1.0.130.20483.nupkg`

工程当前直接引用的 MESAutoLineClient 版本：

- 主工程：`packages\MESAutoLineClient.1.0.129.20464\lib\net40\MESAutoLineClient.dll`
- MES 工程：`packages\MESAutoLineClient.1.0.130.20483\lib\net40\MESAutoLineClient.dll`
- 测试工程：`1.0.86.15454`，由 MES 工程输出目录间接引用

新机器恢复时，NuGet 可以从 `third_party\nuget` 还原这三个 MESAutoLineClient 版本。

## 恢复步骤

1. 用 Visual Studio 2022 打开 `BusbarCompressionSystem.sln`。
2. 让 NuGet 读取仓库内的 `NuGet.config`。
3. 还原 `packages\` 和 `third_party\nuget\` 里的离线包。
4. 检查本地 DLL 是否齐全，重点看 `BusbarCompressionSystem\dll\` 和 `third_party\mes\`。
5. 安装海康 MVS 原生运行库，准备与程序位数一致的 `MvCameraControl.dll`。
6. 先编译主工程，再编译相机、MES、数据库相关子项目。

## 已知构建边界

- `Camera` 工程使用 `unsafe` 代码。
- `Fody 6.6.4` 依赖 MSBuild 16 及以上版本。
- `BusbarCompressionSystem.sln` 含有中文路径 `最新测试方案20250506`。
- `MESAutoLineClient` 在仓库中存在多个版本引用，主工程与 MES 辅助工程使用的版本不同。
- 海康相机托管封装依赖原生 `MvCameraControl.dll`，仓库在 `third_party\hikvision-mvs\` 保存了当前环境的 Win64 和 Win32 版本。
- 当前环境的 MVS 原生库路径是 `C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64\MvCameraControl.dll`。

## 下一步优化

- 用 VS2022 对主解决方案做完整编译验证。
- 现场运行机安装海康 MVS SDK，或把对应位数的原生运行库放到程序输出目录。
