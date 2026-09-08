# 简体中文汉化

## 当前实现

语言包位于 `ContentEditor.App/i18n/SimplifiedChinese.lang.yaml`，覆盖已注册的菜单、窗口名称、设置、基础字段和编辑器字段；`SimplifiedChinese.ui.json` 保存硬编码界面文案的译文。界面显示层会在切换语言时重新取译文，游戏资源中的字段名、用户脚本、文件路径以及来自游戏文件或第三方更新记录的动态内容保留原文。

在“编辑 > 设置 > 显示 > 常规 > 语言”选择“简体中文”。用户配置保存在 `%APPDATA%/REE-Content-Editor/ce_config.ini`，对应 `language = SimplifiedChinese`。切换回 English 会恢复内置英文；缺少语言包或翻译条目时也保留英文回退。

项目已带 `fonts/NotoSansSC-Regular.ttf`，由 `UI.ConfigureImgui()` 合并加载。无需安装系统字体。项目文件已配置将 `i18n/*.*` 复制到构建输出目录。

## 添加或维护翻译

1. 查看 `ContentEditor.App/Imgui/i18n/Translations/*.Lang.cs` 中的定义，也可以通过“编辑 > Dump Translations”导出当前可翻译条目。导出英文模板前先切换到 English。
2. 在语言包添加扁平 YAML 键。例如 `Home.Menu_File: '文件'`；带提示的字段使用 `Settings.GamePath.Text` 和 `Settings.GamePath.Tooltip`。硬编码的表现层短语加入 `SimplifiedChinese.ui.json`。
3. 代码中的静态文字使用 `UiText.T`、`UiText.F`、`UiText.Label` 或 `UiText.FormatLabel`；需要 UTF-8 缓冲区时使用对应的 `Utf8`/`LabelUtf8`。标签翻译要保留 ImGui 控件 ID，动态参数使用格式化占位符传入。
4. 保留所有占位符，包括图标的 `{0}` 和路径参数 `{AUTHOR}`、`{BUNDLE_NAME}`、`{COMMAND}`。保留文件扩展名、技术标识符、序列化字段名、资源路径和快捷键；不要在初始化时把可切换语言的显示数组永久改成译文。
5. 修改语言包后重新编译，或复制到程序目录的 `i18n` 文件夹，再切换语言或重启。
6. 执行本地化测试和只读审计，检查条目是否注册、占位符是否一致，以及中英切换和图标标签是否正确。

`Lang.FindTranslatables()` 收集注册的 `TranslatableBase` 和 `TranslatableGroup` 条目；UI 字典处理不属于注册 Lang 的表现层短语。`UiText.F` 和 `UiText.FormatLabel` 会保留动态参数及控件 ID，语言切换时清空 UTF-8 缓存。不要修改游戏内部字段名或资源路径来翻译界面。

## 构建与验证（Windows / PowerShell）

需要 Git、.NET 10 SDK，以及运行程序所需的 .NET 10 Desktop Runtime。

```powershell
git submodule update --init --recursive
dotnet build ContentEditor.App/ContentEditor.App.csproj -c Release -f net10.0-windows
dotnet test ContentEditor.App.Tests/ContentEditor.App.Tests.csproj -c Release --filter FullyQualifiedName~LocalizationTests
dotnet run --project tools/LocalizationAudit -c Release -- . ./build/localization-audit --check
& ./ContentEditor.App/bin/Release/net10.0-windows/ContentEditor.App.exe
```

本机 SDK 安装在 `%LOCALAPPDATA%/REE-Content-Editor/dotnet`，未修改全局 PATH。若默认 `dotnet` 仍是 9.x，请用以下命令代替上面的 `dotnet`：

```powershell
& "$env:LOCALAPPDATA/REE-Content-Editor/dotnet/dotnet.exe" build ContentEditor.App/ContentEditor.App.csproj -c Release -f net10.0-windows
```

## 本次验收（2026-09-07）

- 审计覆盖 679 个注册条目和 1284 个 UI 源文案，缺失均为 0；UI 字典有 1334 个唯一键。格式缩写、内部标识符等保留原文，静态覆盖不等于所有运行时界面均已目视验收。
- 16 项本地化测试通过，包含占位符、重复键、UTF-8 缓存、语言切换、上下文查找和 ImGui 控件 ID。
- 全量测试 22 项中 21 项通过。`ResourceManager_BundleFileLoadFailure_RecoversFromBaseFileAndDiff` 仍失败；已在未修改的 `d449376a` 基线上复现相同失败，与本次汉化无关。
- 用户人工验收反馈大部分界面汉化正常。之后补充的通用控件、复制提示和后台任务文案经过静态检查、测试和编译，未再进行窗口操作。
- Windows Release 验收时发布到 `build/zh-CN/ContentEditor.App.exe`。该生成目录已在 2026-09-08 工作区迁移清理时移除；新电脑请按上方命令重新构建后运行。

## Git 远程

- `origin`: https://github.com/yequ172672/REE-Content-Editor-cn.git
- `upstream`: https://github.com/kagenocookie/REE-Content-Editor
- 本地 `master` 跟踪 `origin/master`。保留原始历史及子模块固定版本。
