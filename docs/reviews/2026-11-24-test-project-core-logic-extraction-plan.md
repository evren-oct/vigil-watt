# 测试工程去 Compile Include：抽取 VigilWatt.Core.Logic

> 状态：方案，待审查；未实施。
> 基线：2026-11-24，远程 `dev`；当前测试工程 `VigilWatt.Core.Tests`。
> 目标：消除测试工程中 150 条指向 `VigilWatt.Core` 的 `<Compile Include>`，把可测纯逻辑收敛到独立项目。

## 1. 背景

当前 `src/tests/VigilWatt.Core.Tests/VigilWatt.Core.Tests.csproj` 用 159 条显式 `<Compile Include>` 链接源码：

| 来源 | 数量 |
|---|---:|
| `VigilWatt.Core` | 150 |
| `VigilWatt.App` | 6 |
| `VigilWatt.ProtocolCapture` | 3 |

Core 的 150 条分布：

| 目录 | 数量 |
|---|---:|
| `Plugins` | 86 |
| `Capture` | 27 |
| `Models` | 13 |
| `Devices` | 6 |
| `Settings` | 4 |
| `Utils` | 4 |
| `Ble` | 3 |
| `Hid` | 3 |
| `Power` | 2 |
| `Media` | 1 |
| `Storage` | 1 |

测试工程还包含 7 个 `EmbeddedResource` 和 4 个包引用。

问题不是测试跑得慢，而是测试工程实际上在第二次编译大半个 Core。新增纯逻辑文件必须手工记得加进清单；漏了就会出现“代码在 Core 里、测试根本没编译它”的情况。

## 2. 目标

- 测试工程不再出现指向 `VigilWatt.Core` 的 `<Compile Include>`。
- 新建 `VigilWatt.Core.Logic`，目标 `net8.0`，零 WinRT。
- `VigilWatt.Core.Logic` 承载当前测试工程已经证明可在 `net8.0` 编译的 150 个 Core 纯逻辑文件。
- `VigilWatt.Core` 和 `VigilWatt.Core.Tests` 都引用 `VigilWatt.Core.Logic`。
- 测试工程最终只保留 6 个 App 纯逻辑文件和 3 个 ProtocolCapture 文件的显式链接，共 9 条。
- 不改业务行为、不改协议、不改测试断言、不升级测试包。

## 3. 目标依赖方向

```text
VigilWatt.Core.Logic  (net8.0，零 WinRT)
      ↑                    ↑
VigilWatt.Core       VigilWatt.Core.Tests (net8.0)
      ↑
VigilWatt.App
```

约束：

- `VigilWatt.Core.Logic` 不引用 `VigilWatt.Core`、`VigilWatt.App` 或 WinUI。
- `VigilWatt.Core` 引用 `VigilWatt.Core.Logic`。
- `VigilWatt.App` 继续只引用 `VigilWatt.Core`，通过传递依赖获得 Logic。
- 测试工程只引用 `VigilWatt.Core.Logic`，不引用 Windows 目标的 Core/App。

## 4. 迁移边界

只迁移当前测试工程已经链接的 150 个 Core 文件。

这 150 个文件已经被测试工程证明能在 `net8.0` 下编译，因此它们是天然的迁移边界。不要顺手把 Core 里其它文件也搬过去，避免扩大范围。

迁移规则：

- 物理移动到 `src/VigilWatt.Core.Logic/`。
- 保留原有子目录结构，例如 `Plugins/`、`Capture/`、`Models/`、`Storage/`。
- 保留原有命名空间 `VigilWatt.Core.*`，避免大面积改 `using`。
- 从 `VigilWatt.Core` 项目中移除这些文件，禁止两个项目同时编译同一类型。
- 不移动测试工程没有链接的 Core 文件。
- 不移动 `VigilWatt.BleScan`、`VigilWatt.ProtocolCapture` 工程本身。

## 5. 新项目

新增 `src/VigilWatt.Core.Logic/VigilWatt.Core.Logic.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>VigilWatt.Core</RootNamespace>
    <AssemblyName>VigilWatt.Core.Logic</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.12" />
  </ItemGroup>

  <ItemGroup>
    <!-- 把当前测试工程里的 7 个 EmbeddedResource 原样迁过来，
         LogicalName 必须保持不变。 -->
  </ItemGroup>
</Project>
```

新增 `src/VigilWatt.Core.Logic/AssemblyInfo.cs`：

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("VigilWatt.Core.Tests")]
```

`Microsoft.Data.Sqlite` 只被 `HistoryStore` 使用，包引用从测试工程迁到 Logic。测试工程不再直接引用 SQLite 类型。

## 6. 项目引用调整

### VigilWatt.Core.csproj

增加：

```xml
<ItemGroup>
  <ProjectReference Include="..\VigilWatt.Core.Logic\VigilWatt.Core.Logic.csproj" />
</ItemGroup>
```

并确保已迁移的 150 个文件不再由 Core 编译。

### VigilWatt.Core.Tests.csproj

删除：

- 150 条指向 `VigilWatt.Core` 的 `<Compile Include>`
- 7 个 `<EmbeddedResource>`
- `Microsoft.Data.Sqlite` 包引用（如果测试代码没有直接使用 SQLite 类型）

增加：

```xml
<ItemGroup>
  <ProjectReference Include="..\..\VigilWatt.Core.Logic\VigilWatt.Core.Logic.csproj" />
</ItemGroup>
```

保留：

- 6 个 App 纯逻辑文件的 `<Compile Include>`
- 3 个 ProtocolCapture 文件的 `<Compile Include>`
- `RestoreTestHostIntegrityLevel` 构建目标
- xUnit / Test SDK 包引用

### VigilWatt.App.csproj

原则上不改。App 继续引用 Core，Core 传递引用 Logic。

### VigilWatt.sln

新增 `VigilWatt.Core.Logic` 项目。sln 的 GUID 是手写的，新增后要验证 VS 能正常打开、项目依赖顺序正确。

## 7. 资源迁移

当前测试工程里的 7 个 `EmbeddedResource` 必须整体迁到 `VigilWatt.Core.Logic`：

- Betop 目录
- Betop 协议
- Mchose 目录
- Rapoo 目录
- Rapoo 协议
- Mchose 协议
- Logitech HID++ 目录

要求：

- `LogicalName` 保持完全不变。
- 资源随 `VigilWatt.Core.Logic` 程序集输出。
- `ProtocolSpecLoader` 等代码仍按原逻辑名读取。
- 迁移后 `ProtocolSpecLoaderTests`、`BetopCatalogTests`、`RapooSpecTests`、`Mchose*`、`Logitech*` 必须全部通过。

## 8. 实施阶段

### 阶段 0：冻结基线

- 记录当前测试总数：1983。
- 记录当前全量结果：1982 通过 / 1 跳过 / 0 失败。
- 导出 150 个 Core 文件清单，作为迁移核对表。
- 不改任何代码。

### 阶段 1：建立 Core.Logic

- 新建项目。
- 用 `git mv` 迁移 150 个文件，保留目录结构。
- 迁移 7 个资源。
- 迁移 `Microsoft.Data.Sqlite` 包引用。
- 添加 `InternalsVisibleTo`。
- 加入 solution。
- 只构建 `VigilWatt.Core.Logic`，确认 0 警告 0 错误。

### 阶段 2：Core 引用 Logic

- `VigilWatt.Core` 增加 ProjectReference。
- 确认已迁移文件不再属于 Core。
- 构建 `VigilWatt.Core`，确认没有重复类型、没有缺失引用。
- 构建 `VigilWatt.App`，确认传递引用正常。

### 阶段 3：测试工程切换

- 测试工程删除 150 条 Core `<Compile Include>`。
- 删除 7 个资源项。
- 增加对 `VigilWatt.Core.Logic` 的 ProjectReference。
- 保留 App/ProtocolCapture 的 9 条链接。
- 构建测试工程，确认 0 警告 0 错误。
- 跑全量测试，确认 1983 项结果与基线一致。

### 阶段 4：文档与守护

- 更新 `docs/project/architecture.md`，加入 `Core.Logic` 层和依赖方向。
- 更新 `docs/dev/build.md` §4.1：测试边界改为 `Core.Logic`，不再说“链接清单就是边界”。
- 更新 `docs/dev/review-standard.md` R8：新增纯逻辑优先放 `Core.Logic`。
- 可选：增加一个只读检查脚本，检查纯逻辑文件是否误留在 Core 或漏进 Logic。

## 9. 验收标准

必须全部满足：

- 测试工程中指向 `VigilWatt.Core` 的 `<Compile Include>` 数量为 0。
- 测试工程 `<Compile Include>` 总数不超过 9 条。
- `VigilWatt.Core.Logic` 的目标框架是 `net8.0`，不引用 WinRT / WinUI / Windows SDK。
- `VigilWatt.Core.Logic` 不引用 `VigilWatt.Core` 或 `VigilWatt.App`。
- `VigilWatt.Core` 和 `VigilWatt.Core.Tests` 都引用 Logic。
- 7 个资源的 `LogicalName` 未变。
- `InternalsVisibleTo("VigilWatt.Core.Tests")` 生效，internal 类型测试可编译。
- `dotnet build VigilWatt.sln -c Release`：0 警告 0 错误。
- 全量测试：1983 项，1982 通过 / 1 跳过 / 0 失败。
- 没有重复类型定义。
- App 发布产物和运行时行为不变。

## 10. 风险与回滚

主要风险：

- 150 个文件迁移时漏文件或多文件。
- 资源逻辑名写错，导致协议目录加载失败。
- internal 类型没有 `InternalsVisibleTo`，测试编译失败。
- Core 和 Logic 同时编译同一文件，出现重复类型。
- 解决方案文件 GUID / 项目顺序写错，VS 打开异常。

控制方式：

- 以当前测试工程的 150 条清单作为唯一迁移清单。
- 每阶段单独构建，不一次性全改。
- 资源逻辑名逐条对照。
- 全量测试作为最终闸门。
- 在独立分支/worktree 做，不混入 Beta 发版改动。

回滚方式：

- 删除 `VigilWatt.Core.Logic` 项目。
- 把 150 个文件移回 `VigilWatt.Core`。
- 恢复测试工程的 `<Compile Include>` 和资源项。
- 恢复 solution 和项目引用。
- 因为不改业务代码，回滚成本可控。

## 11. 明确不做

- 不改任何协议解析、读数、离线、历史库逻辑。
- 不改任何测试断言。
- 不升级 xUnit / Test SDK / Microsoft.Data.Sqlite 版本。
- 不开启测试并行。
- 不拆成多个测试项目。
- 不把 `VigilWatt.App` 的 6 个文件一起迁移；后续可单独抽 `VigilWatt.App.Logic`。
- 不把 `VigilWatt.ProtocolCapture` 的 3 个文件一起迁移。
- 不用 `..\..\VigilWatt.Core\**\*.cs` 这种 glob 直接替代清单。

## 12. 建议时机

这是结构性重构，不是 Beta 发布阻塞项。

建议：

- Beta 前：只做只读检查，或加一个“漏链检查”脚本。
- Beta 后：单独开分支/worktree 实施 `Core.Logic` 抽取。
- 实施前先审查本方案和 150 文件清单，确认迁移边界。
