# 薄荷启动器架构骨架

> 状态说明（2026-09）：下文记录早期原型的可替换接口设计。当前唯一入口为 `MainWindow`；真实目录导入、版本列表、安装和离线启动通过 `MainWindow.Real.cs` 调用 `Operational/LauncherWorkspace`。`Infrastructure/Prototype` 暂时只承担皮肤校验等尚未迁移的接口，不能把其中的演示数据当作已安装游戏或已登录账户。当前默认预览由代码定义的纯色 3D 模型生成，Steve/Alex 仅决定手臂宽度。后续应继续收敛接口边界。

目标是保留完全独立的界面与业务决策，同时让账户、下载、运行环境和启动实现都可替换。

```text
Presentation (WPF / ViewModel)
              ↓
Core.Abstractions（稳定契约）
              ↓
Core.Services（启动器业务编排）
              ↓
Infrastructure（可替换实现）
```

## 当前边界

- `Core/Models`：与 UI 无关的账户、游戏、Java、下载计划、皮肤、诊断和启动模型。
- `Core/Abstractions`：UI 和业务层唯一依赖的后端接口。
- `Core/Services/LauncherBackend`：负责“检查 → 选择运行时 → 选择下载源 → 诊断 → 启动”的编排。
- `Infrastructure/Prototype`：不会联网或启动进程的原型实现，可逐个替换为真实实现。
- `Composition/AppCompositionRoot`：决定实际使用哪些实现；更换底层时不修改 UI。
- `Presentation/MainWindowViewModel`：把后端状态暴露给界面。

## 下载源策略

下载能力不是绑定某个启动器，而是通过 `IDownloadSourceProvider` 提供。当前目录登记了 Mojang、BMCLAPI、Modrinth 和 CurseForge；它只描述能力与优先级，尚未执行网络请求。

下载页通过 `ILauncherBackend.CreateDownloadPlanAsync` 生成内存计划，界面不直接接触 URL。后续真实下载器可以在不改页面的前提下增加断点续传、校验、镜像故障切换与任务队列。

版本浏览使用 `IVersionCatalogProvider`。当前 `PrototypeVersionCatalogProvider` 返回多条明确用于版式演示的数据，不声明它们是当前可下载的完整版本清单；接入真实元数据时只替换提供者。搜索与快照过滤在展示层完成。

真实实现应继续拆分为元数据解析、URL 重写、并发下载、SHA-1/SHA-256 校验、缓存、失败切换和限速策略。CurseForge 需要独立 API Key 配置，不应写死在客户端源码中。

## 皮肤边界

`ISkinProvider` 将本地预览校验与实际应用隔开。原型读取 PNG 尺寸并向 UI 返回预览结果，Presentation 使用 WPF `Viewport3D` 按标准皮肤 UV 映射生成可旋转方块人；微软账户上传和离线皮肤加载仍应作为不同基础设施实现接入。任何真实上传都必须在界面中明确显示账户与目标，不能把本地预览等同于上传成功。

首页和皮肤工作台使用同一皮肤贴图；首页固定正面，工作台有独立旋转变换。未选择文件时显示打包的 Mojang 默认 Steve/Alex 模板，而非生成或伪造账户皮肤。`LauncherAppearancePreferences` 仅保存本机 UI 偏好（主题、皮肤文件路径、背景图路径和遮罩强度），不属于账户凭据或游戏启动后端。背景图片按比例覆盖全窗口；主题渐变形成可调全局遮罩，主要内容面板另有半透明表面以保证阅读。偏好文件损坏或背景丢失时自动回退默认外观。

## 第一条真实纵向链路

1. 扫描本地 `.minecraft` 与 Java。
2. 读取一个已有原版游戏。
3. 创建离线档案并安全持久化。
4. 生成启动参数，但先提供“仅验证”模式。
5. 最后才真正创建 Java 进程，并把日志交给诊断模块。

原型阶段不复制 PCL/HMCL 的 UI，也不依赖它们的私有服务。若将来直接复用第三方代码，需要单独审查许可证与分发义务。
