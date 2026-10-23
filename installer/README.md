# SnapFlow 安装版

参考本项目上游 ShareX 21.0.0 的 `ShareX.Setup/InnoSetup/ShareX-setup.iss`，使用 Inno Setup 构建。SnapFlow 使用独立的 AppId，不会覆盖或卸载 ShareX。

安装版为 Windows x64 自包含发布，内置 .NET 9 Desktop Runtime。默认安装到 `%LOCALAPPDATA%\Programs\SnapFlow`，支持选择目录、开始菜单入口、可选桌面快捷方式以及 Windows 应用列表卸载。仅为当前用户安装，不请求管理员权限；选择的目录需要当前用户有写入权限。设置沿用程序目录存储方式。

安装包不包含个人设置；首次运行使用程序默认值。覆盖升级保留目标电脑已有的设置，卸载仅移除安装文件和快捷方式，保留运行后生成的设置。程序运行时安装器会提示先正常退出，不强制终止进程。未提供代码签名或自动更新功能。

不添加开机启动、托盘常驻、后台服务或计划任务。用户打开程序时在前台显示主窗口，关闭主窗口即退出进程；安装完成后的启动选项默认不勾选。

安装 Inno Setup 6.5 或更新版本及支持 net9.0-windows 的 .NET SDK，在项目根目录运行：

```powershell
.\build-installer.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

脚本生成 `dist/SnapFlow-<版本>-setup-x64.exe`、对应的 `SnapFlow-<版本>-source.zip` 和文件哈希清单。便携版仍由 `build-capture.ps1` 单独构建。每次使用新的隔离发布目录，检查并排除个人配置、日志及调试符号。

简繁中文语言文件来自 [Inno Setup 官方翻译索引](https://jrsoftware.org/files/istrans/) 指向的官方源码仓库 `Files/Languages/ChineseSimplified.isl` 和 `ChineseTraditional.isl`，保留原文件中的贡献者说明；文件声明适用于 Inno Setup 6.5.0+。英文使用编译器自带的 Default.isl。
