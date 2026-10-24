<p align="center">
  <img src="src/ShareX.Roll/Assets/SnapFlow.svg" alt="SnapFlow 软件图标" width="112" height="112">
</p>

<h1 align="center">SnapFlow</h1>

<p align="center">不求面面俱到，只求滚动截图简单好用。</p>

<p align="center">
  <a href="https://github.com/shrnbshr/SnapFlow/stargazers"><img src="https://img.shields.io/github/stars/shrnbshr/SnapFlow?style=social&amp;logo=github&amp;label=Stars" alt="GitHub Star 数量"></a>
  <a href="https://github.com/shrnbshr/SnapFlow/releases/latest"><img src="https://img.shields.io/github/v/release/shrnbshr/SnapFlow?style=plastic&amp;label=release&amp;color=blue" alt="最新 Release 版本"></a>
  <a href="https://github.com/shrnbshr/SnapFlow/search?l=c%23"><img src="https://img.shields.io/github/languages/top/shrnbshr/SnapFlow?style=plastic&amp;color=blue" alt="主要编程语言与占比"></a>
</p>

## 项目介绍

轻量的 Windows 截图与滚动截图工具，支持区域、窗口和全屏捕获，以及长图预览、裁剪、批注与本地导出。

基于 [ShareX](https://github.com/ShareX/ShareX) 的截图能力构建，使用 WinForms 和 Windows 原生窗口，提供集中的截图工作台。

[下载最新版](https://github.com/shrnbshr/SnapFlow/releases/latest) · [历史版本与更新记录](https://github.com/shrnbshr/SnapFlow/releases) · [反馈问题](https://github.com/shrnbshr/SnapFlow/issues)

## 功能

| 功能 | 说明 |
| --- | --- |
| 区域与窗口截图 | 拖动框选区域，或单击捕获窗口、控件 |
| 全屏截图 | 截取整个桌面，在主窗口预览结果 |
| 滚动截图 | 自动滚动并拼接长图，适合文章、列表等可滚动内容 |
| 图片预览 | 缩放、拖动查看、适合窗口与适合宽度，方便阅读长截图 |
| 裁剪与批注 | 拖动图片边界裁剪，添加文字、箭头、形状、编号、模糊或马赛克 |
| 复制与导出 | 复制到剪贴板，或保存为 PNG、JPEG、GIF、BMP、TIFF |
| 保存窗口默认文件夹 | 可指定每次打开保存窗口时首先进入的文件夹，重启后保留，仍需手动确认保存 |
| 个性设置 | 自定义全局快捷键，选择浅色、深色或跟随系统，调整语言与滚动参数 |

截图在本地处理，复制、保存和导出均使用完整分辨率的图片。

## 下载与运行

通过下方链接下载最新版本，更新内容见 [发行说明](https://github.com/shrnbshr/SnapFlow/releases/latest)。

- [Windows x64 安装版（推荐）](https://github.com/shrnbshr/SnapFlow/releases/latest/download/SnapFlow-setup-x64.exe)
- [Windows x64 便携版](https://github.com/shrnbshr/SnapFlow/releases/latest/download/SnapFlow-win-x64.zip)
- [对应版本源码包](https://github.com/shrnbshr/SnapFlow/releases/latest/download/SnapFlow-source.zip)

安装版按提示完成安装后即可使用。便携版完整解压后，运行其中的 `SnapFlow.exe`，并保留同目录的 DLL 和语言资源。建议将便携版解压到具有写入权限的目录，以便保存设置。

两种版本均面向 **Windows x64**。安装版内置 .NET 运行时，无需单独安装；便携版需要 **.NET 9 Desktop Runtime**。不同发行版本的运行要求以对应 Release 的说明为准。

## 使用

### 截图

在主窗口左侧选择截图方式：

- **区域/窗口截图**：拖动框选，或在高亮的窗口、控件上单击。
- **全屏截图**：捕获整个桌面。
- **滚动截图**：选择需要滚动的正文或列表区域，等待捕获完成。

截图结果显示在右侧预览区，可直接复制、保存，或进入编辑窗口。选区时按 `Esc` 取消。

### 滚动截图

1. 将目标内容滚动到需要的起始位置。
2. 选择正文或列表区域，尽量避开地址栏、固定导航和无关侧栏。
3. 开始后保持目标窗口可见，等待完成；需要提前结束时，再按一次滚动截图快捷键。
4. 在主窗口查看长图，复制或保存结果。

在右上角 **设置 → 滚动截图** 中可调整延迟、滚动间隔、滚动步长和自动回到顶部等选项。内容无法滚动或画面没有变化时，程序会提示未检测到滚动。

滚动捕获效果取决于目标程序的滚动方式与页面布局。遇到固定悬浮控件、动态加载或复杂嵌套区域时，可尝试重新框选范围、调整滚动参数，或分段截图。

### 预览与编辑

- **预览**：使用滚轮或缩放控件调整比例，放大后可拖动查看；点击比例数字或按 `0` 恢复整图适配。
- **阅读长图**：选择“适合宽度”，使用滚轮、滚动条或 `Page Up` / `Page Down` 上下阅读，`Home` / `End` 跳到首尾；`Ctrl + 滚轮` 调整缩放。
- **裁剪**：点击 **编辑**，拖动图片四边或四角调整范围，按 `Enter` 确认，`Esc` 取消本次裁剪。
- **批注**：选择文字、箭头、画笔、形状等工具，可调整颜色，并使用 `Ctrl + Z` / `Ctrl + Y` 撤销或重做。
- **输出**：在编辑窗口复制、保存或导出；点击 **应用到预览**，将编辑结果带回主窗口。

### 快捷键与设置

| 功能 | 默认快捷键 |
| --- | --- |
| 区域/窗口截图 | `Ctrl + Shift + A` |
| 全屏截图 | `Ctrl + Shift + F` |
| 滚动截图 / 停止滚动截图 | `Ctrl + Shift + S` |
| 打开设置 | `Ctrl + ,` |

可在 **设置** 中修改快捷键。若默认滚动快捷键被占用，程序会尝试 `Ctrl + Alt + S`；实际注册结果以主窗口提示为准。

设置页还提供外观、语言和界面动画选项。修改后点击 **保存并返回** 生效；取消则放弃未保存的修改。

在 **设置 → 截图与保存** 中开启 **指定保存窗口的默认文件夹**，点击 **浏览…** 选择文件夹，再保存设置。此选项默认关闭；开启后，主窗口保存、滚动截图保存和编辑器导出都会先打开指定文件夹。临时保存到其他位置不会改变此设置，重启后仍会使用指定路径。它不会在截图后自动保存；指定文件夹失效时，保存窗口会在标题中提示，并回退到其他可用位置。

V1.0.2 仅提供简体中文、香港繁体中文和英文。默认跟随 Windows，也可手动选择；繁体中文系统使用香港繁体中文，其他非中文系统使用英文。主程序、截图及编辑组件均保留对应的三种语言资源，源码与发布包中的其余语言资源已移除。旧版已保存的其他语言选择会回退为跟随 Windows，并保留其他设置。

设置保存在程序目录的 `SnapFlow.preferences.json` 和 `SnapFlow.settings.json` 中。更新便携版前可先备份这两个文件，保留自己的偏好与滚动配置。

## 从源码构建

在 Windows 上安装支持项目目标框架的 .NET SDK，然后在项目根目录运行：

```powershell
.\build-capture.ps1
```

当前项目目标框架为 `net9.0-windows`。脚本生成：

| 路径 | 内容 |
| --- | --- |
| `dist/SnapFlow/` | 可运行的程序目录 |
| `dist/SnapFlow-win-x64.zip` | 便携分发包 |
| `dist/SnapFlow-source.zip` | 对应源码、构建脚本、测试和许可证 |

程序运行时请先正常退出，再重新构建。构建脚本会保留本地设置，分发包不包含个人配置。

需要生成安装版时，安装 Inno Setup 6.5 或更新版本，然后运行 `./build-installer.ps1`（可用 `-IsccPath` 指定 `ISCC.exe`）。脚本生成 `dist/SnapFlow-<版本>-setup-x64.exe` 和对应版本源码包。安装版内置 .NET 运行时，默认安装到当前用户目录，提供开始菜单入口、可选桌面快捷方式及卸载功能；不包含个人配置。详见 [安装版说明](installer/README.md)。

源码结构：

- `src/ShareX.Roll/`：SnapFlow 主程序、工作台、设置与编辑器集成。
- `src/ShareX.ScreenCaptureLib/`：选区、截图、滚动捕获与编辑器相关代码。
- `src/ShareX.HelpersLib/`、`src/ShareX.ImageEffectsLib/`、`src/Libs/`：公共代码、资源和依赖。
- `src/ShareX.Roll.Tests/`、`tests/`：回归测试与测试素材。

验证记录与已知限制见 [VALIDATION.md](VALIDATION.md)。需要运行桌面截图冒烟测试时，可使用：

```powershell
.\build-capture.ps1 -DesktopSmoke
```

该测试需要可交互的 Windows 桌面，会执行实际截图。

## 问题反馈

请通过 [Issues](https://github.com/shrnbshr/SnapFlow/issues) 提交问题或建议。报告问题时，请注明 SnapFlow 版本、Windows 版本、显示缩放比例、复现步骤，以及预期和实际结果。滚动截图问题请同时说明目标程序、页面布局与滚动参数。

## 来源与许可

SnapFlow 是基于 **ShareX 21.0.0** 裁剪与修改的非官方项目。这里的版本号表示上游源码基线；SnapFlow 自身的发行版本见 [Releases](https://github.com/shrnbshr/SnapFlow/releases)。

感谢 ShareX 团队及相关开源项目。来源、修改范围与依赖许可见 [NOTICE.md](NOTICE.md)、[src/UPSTREAM.json](src/UPSTREAM.json) 和 [Licenses/](Licenses/)。

本项目采用 **GPL-3.0-or-later**，详见 [LICENSE.txt](LICENSE.txt)。分发二进制时应同时提供对应源码，并保留原作者署名与许可证。
