# 安装与连接

## 1. 准备软件

Windows x64、已安装并能正常打开的 AutoCAD 2026（R25.1）、.NET 8 x64 Runtime。使用源码构建时再安装 .NET 8 SDK。演示推荐新建 acadiso.dwt 公制图纸。

## 2. 安装 MCP 和 Skills

下载 [1.1.0 Windows x64 安装包](../releases/codex-autocad-mcp-2d-1.1.0-win-x64.zip) 并完整解压，在解压后的工具包根目录打开 PowerShell 运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\plugin\scripts\install-package.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\plugin\scripts\install-skills.ps1
```

更新插件前关闭 AutoCAD；脚本不自动关闭图纸。已有插件备份到同级 `AiCad.bundle.backup-...`。两套新技能安装到 `$CODEX_HOME/skills` 或当前用户 `.codex/skills`，已有同名技能备份到 `skill-backups`。

如果下载的是 Git 源码而非编译包，先设置实际 AutoCAD 路径并构建安装：

```powershell
$env:AUTOCAD_PATH = 'D:\cad\AutoCAD 2026' # 替换为自己的安装路径
powershell -NoProfile -ExecutionPolicy Bypass -File .\plugin\scripts\install-user.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\plugin\scripts\install-skills.ps1
```

源码安装包含测试和发布包检查，源码输出留在本工具包目录。

## 3. 连接 Codex

插件路径为 `%APPDATA%\Autodesk\ApplicationPlugins\AiCad.bundle`。启动/重启 AutoCAD，打开公制新图，命令行运行 `AICADSTATUS`，应出现 `AICAD_READY`。

MCP 配置使用 STDIO，command 填安装脚本输出的 `Contents\Bridge\AiCad.Bridge.exe` 完整路径，服务名 `ai_cad`。参考 [配置样例](../plugin/scripts/codex-mcp.example.toml)，替换 `<USER>`，只合并这个服务，保留其他设置。若此前已注册同路径服务，刷新该服务即可。新 Skills 需要刷新技能列表或新开会话才能发现。

样例把创建、读取和新文件保存设为 approve，修改已有图形和撤销仍为 prompt。实际授权与客户端策略优先。字段依据 [官方 Codex MCP 文档](https://learn.chatgpt.com/docs/extend/mcp?surface=cli)。

发给 Codex：

```text
使用 $autocad-mcp-drawing。先实际调用 cad_health、cad_get_context、cad_get_layers，告诉我当前图纸与单位，检查是否有 cad_draw_2d、cad_get_geometry、cad_save_drawing。连接成功后先等待我给绘图尺寸。
```

health 插件版本应为 1.1.0.0；完整工具列表有17项。只加载 Skills 不会升级 Native 插件；只刷新 Codex 不会升级 AutoCAD 中已加载的 DLL。教学时保持一个目标 AutoCAD 实例、命令行空闲。

## 4. 录制复杂图形教学

把 [案例提示词](../examples/mcp-stepped-flange/prompts.txt) 按阶段发给 Codex。阶段模板中的空指纹必须替换为当次 context，固定演示 GUID 必须换成新意图的 GUID；重试则保留当次原计划和 GUID。

默认参数为220×160，20处R8，中央Ø60，Ø90节圆上6×Ø10，4×Ø9，两处30×12长圆槽。完成后读取实际几何/measurement，再保存到新的绝对路径。参考成图可直接打开 [flange_mcp.dwg](../examples/mcp-stepped-flange/flange_mcp.dwg)。

视图缩放由演示者操作，保持画面可见。尺寸为原生 DIMENSION，但不是关联轮廓句柄的全自动更新尺寸；改轮廓后同步复核标注。

## 5. 本机复测

```powershell
$env:AUTOCAD_PATH = 'D:\cad\AutoCAD 2026'
powershell -NoProfile -ExecutionPolicy Bypass -File .\plugin\scripts\build.ps1
python .\plugin\tests\AutoCAD.Integration\run-mcp-2d.py --template '你的acadiso.dwt绝对路径' --stages .\examples\mcp-stepped-flange --output .\plugin\TestResults\my-new-test
```

测试输出目录需不存在。独立DXF校核/渲染需要 Python 的 ezdxf 和 matplotlib：

```powershell
python -m pip install ezdxf matplotlib
python .\examples\mcp-stepped-flange\verify-dxf.py .\examples\mcp-stepped-flange\flange_mcp.dxf --preview .\examples\mcp-stepped-flange\preview.png --report .\examples\mcp-stepped-flange\verification.json
```

早期独立 LSP 路线仍在 `examples/stepped-flange/`：新图 APPLOAD 加载 flange_demo.lsp，再依次运行 CADOUTLINE/CADHOLES/CADDIMS。

## 6. 已有 AutoLISP 自动绘图路线

需要 Windows AutoCAD。默认案例已在 AutoCAD 2026 验证。在 Codex 打开仓库，明确选择 AutoLISP 路线，再提交 [自动绘图课程](autolisp-auto-drawing.md) 中的完整绘图提示词。由 Codex 确认实际环境、检查尺寸、创建 `practice/flange_demo.lsp`，然后在 AutoCAD 新空白图纸中自动执行轮廓、孔槽与尺寸三个阶段。你不需要手动 APPLOAD 或输入阶段命令。

每个阶段自动 REGEN、ZOOM，停留3秒并导出原生预览。校核后保存新 DWG、DXF，并用 AutoCAD 原生引擎重开检查。出现尺寸冲突或阶段失败时停止，不改变指定尺寸、不覆盖原图。参考图在 `examples/stepped-flange/`，实际结果见 `practice/`。

此路线使用 AutoCAD 原生 AutoLISP 和 `/b` 脚本入口，不依赖 MCP 插件或 .NET SDK。`skills/codex-autocad-toolkit/scripts/run-visible-lisp.py` 需要 Python 3、ezdxf、Pillow；执行前检查实际 acad.exe、acadiso.dwt 路径。该执行器只负责法兰的三个阶段，其他形状需要按尺寸另写脚本和校核。只有明确选择手动教学时，才使用 APPLOAD 与阶段命令。
