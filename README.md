# Codex AutoCAD 绘图工具包

Codex + AutoCAD 2026 绘图工具包。包含 MCP 插件源码、安装与连接说明、测试，以及阶梯法兰安装板教学案例。

![阶梯法兰教学案例](examples/stepped-flange/preview.png)

## 先选一个入口

- **二维案例演示**：用 AutoLISP 分阶段完成轮廓、孔槽与25个原生尺寸。打开参考 DWG，在新空白图纸中运行案例脚本。不需要先安装 .NET 或 MCP。
- **Codex 直连 AutoCAD**：构建并安装 AI-CAD 插件，连接 MCP Bridge，创建圆角矩形四孔底板、三维实体和二维三视图，并读取图纸证据。

插件直接支持的几何模板是圆角矩形四孔底板；阶梯法兰采用独立 AutoLISP 脚本。复杂需求需要明确尺寸、选择适合的路线并复核结果。

## 下载与接入

1. Code → Download ZIP 并完整解压，或 git clone 此仓库。
2. 在 Codex 打开工具包文件夹，将 `prompts/start-here.txt` 发给 Codex。
3. 按 [接入说明](docs/getting-started.md) 选择案例脚本或 MCP 路线。
4. 检查实际连接和图形，保存自己生成的文件。

## 文件

| 目录 | 用途 |
|---|---|
| plugin/ | AutoCAD .NET 插件、MCP Bridge、安装脚本与测试 |
| examples/stepped-flange/ | LSP、提示词、DWG、DXF、预览与默认参数校核结果 |
| skills/codex-autocad-toolkit/ | 与本仓库配套使用的 Codex Skill 指引 |
| prompts/ | 首次开工提示词 |
| docs/ | 接入和验证说明 |

## 环境与验证

MCP 路线需要 Windows、AutoCAD 2026 和 .NET 8 SDK。软件和授权由使用者自行准备，仓库不分发 Autodesk SDK 或安装程序。
实际执行记录见 [验证说明](docs/verification.md)。本工具包不承诺任意零件的一句话生成或制造验收。

源码来源为原 [CAD-SW-ANAYSIS 的 AiCad 部分](https://github.com/qu347/CAD-SW-ANAYSIS/tree/main/AiCad)。本次只提取 AutoCAD 相关内容；文件来源和必要路径调整见 extraction-manifest.json。
