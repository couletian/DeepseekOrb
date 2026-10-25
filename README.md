<img width="1016" height="1142" alt="屏幕截图 2026-10-25 215021" src="https://github.com/user-attachments/assets/96f507f6-573e-4635-b24c-6dadeb1da542" /># DeepSeek 桌面悬浮窗（DeepSeek Orb）

一个轻量级的 Windows 桌面浮窗，把 DeepSeek 网页版常驻在屏幕右下角，
鼠标一移上去就展开，用完自动收回，不占用任务栏位置。

单文件 **1.2 MB**，复用系统已有的 .NET 8 与 WebView2 运行时，不自带 Chromium。

---

## 功能特性

**悬浮球交互**
- 靠近屏幕右下角的半透明方块，鼠标移上去约 0.2 秒展开
- 移出窗口 0.65 秒后自动收回，点击过窗口则保持不收（避免阅读时消失）
- 球与标题栏都是实时抓屏模糊，透出真实桌面的明暗

**窗口内多对话（纵向平铺）**
- 「＋」按钮新开对话面板，新面板出现在最上方，所有面板纵向均分空间
- 每个面板是独立的 DeepSeek 页面，可同时进行不同对话
- 面板之间的细线可拖动，调整相邻面板的高度比例
- 拖动窗口边框时，内部面板同步缩放

**窗口尺寸：优先纵向**
- 默认窄而高（约 420×860），只占纵向空间，不横向展开
- 最小可缩到 280×220

**记忆**
- 窗口尺寸、面板数量、各面板高度比例、网页缩放
- 全部面板共用一份登录态，不需要重复登录

**其他**
- 单实例运行，重复启动只会聚焦已有实例
- 主题跟随系统（浅色 / 深色）
- 磨砂背景用抓屏 + 盒式模糊实现，不依赖第三方 UI 库

---

## 运行要求

| 依赖 | 说明 |
|---|---|
| Windows 10 / 11 | 64 位 |
| .NET 8 Desktop Runtime | [下载](https://dotnet.microsoft.com/download/dotnet/8.0) |
| WebView2 Runtime | Windows 11 自带；Windows 10 需[单独安装](https://developer.microsoft.com/microsoft-edge/webview2/) |

---

## 快速开始

双击 `DeepSeekOrb.exe`。

启动后屏幕右下角出现半透明方块，同时在后台加载 DeepSeek（首次约 5–8 秒）。
鼠标移上去即可开始对话。

**标题栏按钮**

| 按钮 | 作用 |
|---|---|
| ← | 后退 |
| ⟳ | 刷新 |
| ＋ | 增加对话面板 |
| － | 移除最后一个面板 |
| 📌 | 固定窗口（不自动收起） |
| ⌄ | 收起成方块 |
| ✕ | 退出程序 |

---

## 构建

需要 .NET SDK 8.0 或更高。

```powershell
dotnet publish src/DeepSeekOrb.csproj `
  -c Release -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=none `
  -p:DebugSymbols=false `
  -o dist
```

产物 `dist/DeepSeekOrb.exe` 约 1.2 MB。

`src/NuGet.config` 已配置国内镜像（华为云），网络不通时可自行更换源。

---

## 项目结构

```
src/
├── App.xaml / App.xaml.cs          启动流程、单实例、主题色
├── MainWindow.xaml / .cs           主窗口、标题栏、纵向平铺布局、展开收起
├── OrbWindow.xaml / .cs            右下角悬浮球、悬停展开
├── ConversationPane.xaml / .cs     单个对话面板（WebView2 + 标题条）
├── WebHost.cs                      共享的 WebView2 环境（多面板共用进程与登录态）
├── Settings.cs                     窗口尺寸、面板比例、缩放的记忆存取
├── ScreenCapture.cs                抓屏 + 盒式模糊（磨砂背景）
├── Native.cs                       Win32 调用（DWM 圆角、accent policy 等）
├── Assets/app.ico                  应用图标
├── DeepSeekOrb.csproj              项目配置
├── app.manifest                    Windows 11 兼容声明
└── NuGet.config                    NuGet 源配置
```

---

## 隐私说明

**本程序不包含任何账号、密码、Cookie 或登录凭据。**

登录态由 WebView2 在首次登录时生成，保存在本机：

```
%LOCALAPPDATA%\DeepSeekOrb\WebView2
```

该目录**不随程序分发**，也不存在于源码或可执行文件中。删除该目录即等于
退出登录并清空全部缓存。

程序本身的行为：
- 不收集、不上传任何个人数据
- 不包含任何遥测或分析组件
- 所有网络请求都发往 `chat.deepseek.com`（由 WebView2 发起）

可执行文件已禁用调试符号（`DebugType=none`），不含源码路径等构建信息。

---

## 资源占用

本机实测（i7-12700H / 2560×1440）：

| 项目 | 数值 |
|---|---|
| 磁盘 | 1.2 MB |
| 内存（1 面板） | ≈460 MB |
| 内存（2 面板） | ≈523 MB（第二个只增 62 MB） |
| 内存（3 面板） | ≈692 MB |
| 空闲 CPU | ≈0.02% |

所有面板共享同一个 WebView2 浏览器进程与登录态，每多开一个面板只增加
一个渲染进程。若改用多个独立程序实例，每个都要独立 Chromium 与独立
用户目录，内存翻倍且需要重复登录。

内存下限由 Chromium 内核决定，无法显著压缩；同一页面在 Chrome 中打开
约 300–400 MB，本方案多出的部分是常驻待命的代价，换取展开无延迟。

---

## 已知限制

1. 磨砂背景是展开瞬间的静态快照，背后窗口内容变化时不会实时刷新；
   收起后再展开即更新。
2. 面板只支持纵向堆叠（单列多行），不支持左右分栏。
3. 单实例运行，多开对话请用标题栏的「＋」按钮。
4. 需要系统已装 .NET 8 Desktop Runtime 与 WebView2 Runtime。

---
## 界面演示
<img width="2558" height="1438" alt="屏幕截图 2026-10-25 214950" src="https://github.com/user-attachments/assets/1547caea-7b38-4200-9182-b0e743309176" />
<img width="2558" height="1438" alt="屏幕截图 2026-10-25 214955" src="https://github.com/user-attachments/assets/cfbca189-c4bd-455f-9a03-032596a0b273" />
<img width="611" height="1150" alt="屏幕截图 2026-10-25 215006" src="https://github.com/user-attachments/assets/4ee9ae18-5fc6-4169-bb8e-f9fccb1133dc" />
<img width="1016" height="1142" alt="屏幕截图 2026-10-25 215021" src="https://github.com/user-attachments/assets/e2f7ab55-4847-42c6-9532-288044667149" />
<img width="611" height="1150" alt="屏幕截图 2026-10-25 215006" src="https://github.com/user-attachments/assets/c9039c18-229e-430d-98fa-7e4bd2b7bdb9" />
<img width="980" height="1110" alt="屏幕截图 2026-10-25 215031" src="https://github.com/user-attachments/assets/5870e696-0165-4098-b10f-346e067dcdbc" />![Uploading 屏幕截图 2026-10-25 214955.png…]()

---

## 已知限制

1. 磨砂背景是展开瞬间的静态快照，背后窗口内容变化时不会实时刷新；
   收起后再展开即更新。
2. 面板只支持纵向堆叠（单列多行），不支持左右分栏。
3. 单实例运行，多开对话请用标题栏的「＋」按钮。
4. 需要系统已装 .NET 8 Desktop Runtime 与 WebView2 Runtime。

---


## 许可

仅供学习交流使用。
