# 接入 Codex 和 AutoCAD

## 从 GitHub 下载

```powershell
git clone https://github.com/qu347/codex-autocad-toolkit.git
cd codex-autocad-toolkit
```

没有 Git 时用仓库页面 Code → Download ZIP，完整解压到独立文件夹。在 Codex 中打开此文件夹，提交 `prompts/start-here.txt` 的内容。

## 先跑二维教学案例

需要 Windows AutoCAD。默认案例已在 AutoCAD 2026 验证。
打开新空白图纸，在命令行运行 APPLOAD，加载 `examples/stepped-flange/flange_demo.lsp`。
依次运行 CADOUTLINE、CADHOLES、CADDIMS，检查结果再另存为自己的 DWG。
参考图和默认参数验证结果位于同一目录。此路线不依赖 MCP 插件或 .NET SDK。

## 接入 MCP 插件

前提为 Windows、AutoCAD 2026 与 .NET 8 SDK。仓库不附带 AutoCAD 软件或授权。
在 PowerShell 中按实际安装位置设置路径；常规安装位置可省略环境变量：

```powershell
$env:AUTOCAD_PATH = 'C:\Program Files\Autodesk\AutoCAD 2026'
powershell -NoProfile -ExecutionPolicy Bypass -File .\plugin\scripts\install-user.ps1
```

安装脚本会构建、运行测试并验证插件包，安装到当前用户的 Autodesk ApplicationPlugins；已有插件会先备份。
重启 AutoCAD，新建图纸并运行 AICADSTATUS，确认出现 AICAD_READY。

在 Codex 的 MCP 服务配置中添加 STDIO 服务，可执行文件使用安装脚本输出的 `AiCad.Bridge.exe` 完整路径。
配置样例在 `plugin/scripts/codex-mcp.example.toml`。只合并本项目配置，替换 `<USER>`，保留已有 MCP 配置。
连接后让 Codex 先调用 cad_health、cad_get_context；实际调用成功后再绘图。

可使用以下任务验证圆角四孔底板：

```text
创建100×60×10 mm圆角底板，圆角R5，四个直径8的通孔，孔中心距两边各10 mm，生成三维实体、三视图和尺寸。
```

插件当前直接支持圆角矩形四孔底板及其孔径修改。读取工具支持图层/实体信息、布局、尺寸、文字表格和块证据；不是任意图形建模接口。
修改孔径和撤销按插件的提议与确认机制执行。

## 自检

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\plugin\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\plugin\tests\AutoCAD.Integration\run-status.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\plugin\tests\AutoCAD.Integration\run-core-console.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\plugin\tests\AutoCAD.Integration\run-pipe.ps1
```

测试只在工具包的 TestResults 中创建测试图纸和加载本地插件，不修改软件安装目录。详见 `docs/verification.md`。
