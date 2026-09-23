# 薄荷启动器（功能预览）

这是独立的 Minecraft 启动器项目。原来的居中启动界面是唯一主界面；版本下载、游戏库、账户登录和目录导入已接入真实操作。尚未实现的整合包与皮肤上传会在界面中明确标出。

## 目前能做什么

- 从 Mojang 版本目录获取正式版，选择并安装原版 Minecraft。首次安装需要联网并下载游戏文件与所需 Java。
- 创建本地离线玩家 ID（默认 `Player`），检查文件后启动已安装的游戏。离线档案无法登录要求正版验证的服务器。
- 使用微软账户在系统浏览器中登录，并尝试完成 Xbox 与 Minecraft Java 身份验证；启动前静默刷新会话。需拥有 Minecraft Java 版。登录密码不经过启动器；令牌缓存与账户数据使用当前 Windows 用户的加密存储。新应用 ID 目前在 Minecraft 服务阶段返回 HTTP 403，尚不能完成正版登录。
- 选择 PCL 或 HMCL 使用的标准 `.minecraft` 目录，读取其中 `versions/<版本>/<版本>.json`。导入操作只引用目录，不复制或修改原文件；选择安装或启动后，文件检查可能补全缺失内容。
- 若导入实例的 MOD 和日志表明它原本使用 Fabric、但版本入口已变成原版，点击“启动游戏”会自动从 Fabric 官方元数据获取对应加载器配置，生成独立的薄荷 Fabric 版本并补全依赖。原实例的 `mods`、存档、HMCL 设置和版本 JSON 保持不变；首次修复还会留下版本 JSON 备份。修复后仍使用原实例的游戏运行目录。
- 在下载页取消正在执行的安装；已经下载的有效文件保留，之后可以继续。

目前不支持 Forge/NeoForge/Quilt 自动修复、整合包 ZIP 导入和皮肤上传。Fabric 自动补全已在临时目录完成真实依赖下载测试，但尚未验证完整游戏进入世界。微软网页登录与本机回调已实测通过，但 Minecraft 服务对新应用 ID 返回 403；需走 Minecraft 的应用授权流程，Azure 注册成功本身并不代表可以使用 Minecraft Services。不应将此版本称作正式可用发行版。

## 运行与构建

运行 `MintLauncher.exe`。构建需要 Windows 和 .NET 10 SDK：

```powershell
dotnet build .\MintLauncher\MintLauncher.csproj -c Release
dotnet run --project .\MintLauncher.Tests\MintLauncher.Tests.csproj -c Release
```

玩家 ID 和游戏目录保存在 `%LOCALAPPDATA%\MintLauncher\workspace.json`；界面外观偏好保存在同目录的 `appearance.json`。默认独立游戏目录为 `%LOCALAPPDATA%\MintLauncher\.minecraft`。

微软应用注册使用个人 Microsoft 账户类型和“移动和桌面应用”平台，回调 URI 为 `http://localhost`；客户端 ID 已写入启动器，无需客户端密钥。账户和令牌只保存在当前 Windows 用户的 `%LOCALAPPDATA%\MintLauncher` 中，不随源码提交。首次登录会打开系统浏览器，微软页面由用户自行完成登录和授权。

当前应用注册已通过微软网页登录与本机回调测试，但 Minecraft Services 返回 HTTP 403。已向 Minecraft 的 AppID Review 提交应用 ID 审核申请，尚未获批；不会改用其他启动器的客户端 ID。审核通过前可继续使用离线档案，但它不能进入要求正版验证的服务器。

## 素材和依赖

未选择皮肤时，首页以代码定义的纯色材质渲染薄荷色 3D 人偶，不内置 Minecraft 或第三方皮肤图片；Steve/Alex 选项仅切换手臂宽度。人偶待机时轻微转身、摆头、摆臂；按住角色拖动可从侧面查看。用户可选择本地 PNG 预览自己的皮肤，但不会自动上传到 Minecraft 账户。未经授权的皮肤图片不随源码发布。

实际安装和启动使用 [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) 4.0.6（MIT）；微软登录使用 [CmlLib.Core.Auth.Microsoft](https://github.com/CmlLib/CmlLib.Core.Auth.Microsoft) 与 MSAL；Fabric 自动补全读取 [Fabric Meta 官方 API](https://github.com/FabricMC/fabric-meta)。PCL、HMCL 源码只用于研究目录格式，未复制到本项目。项目与 Mojang、Microsoft、FabricMC、PCL 和 HMCL 官方均无隶属关系。
