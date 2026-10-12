# 本机连接

本技能依赖 AI-CAD 1.1.0 工具包的本机 STDIO MCP，Windows AutoCAD 2026（R25.1）、.NET 8 x64 Runtime。源码构建额外需要 .NET 8 SDK。

安装包根目录有 `plugin/scripts/install-package.ps1`，先解压完整工具包，再在根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\plugin\scripts\install-package.ps1
```

插件安装到 `%APPDATA%\Autodesk\ApplicationPlugins\AiCad.bundle`，已有版本先备份。重启 AutoCAD，打开公制新图纸，执行 AICADSTATUS 应出现 AICAD_READY。用该目录的 `Contents\Bridge\AiCad.Bridge.exe` 作为 MCP command，服务名 `ai_cad`；参考包内 `plugin/scripts/codex-mcp.example.toml`，只合并这一服务，保留其他配置。

刷新或重启 Codex MCP 服务后，调用 health/context 并确认新四工具实际出现。仅启动 Bridge 不会启动 AutoCAD。多个 AutoCAD 进程同时运行可能使会话选择不确定，教学时先保留目标实例。Native 在 AutoCAD 主线程执行；桌面绘图时保持命令行空闲，不留在交互命令中。

| 现象 | 下一步 |
|---|---|
| 找不到会话/管道 | 确认 AutoCAD 已打开、AICADSTATUS 成功、同一 Windows 用户运行 |
| 只有旧工具 | 更新 bundle；AutoCAD 和 MCP 都刷新，不只修改 Skills |
| UNIT_REQUIRED | 在目标图纸定义单位，再读 context；不缩放猜测已有对象 |
| DOCUMENT_CHANGED | 新阶段重读 context；重试仍保留原请求和原计划 |
| LAYER_CONFLICT | 读实际图层，选不同层名或明确处理要求 |
| OUTPUT_EXISTS | 选新的输出路径；不删除用户现有文件 |

教学案例的三阶段模板位于工具包 `examples/mcp-stepped-flange/`。安装后的 Skill 不依赖其自身目录之外的相对文件；用户未给工具包位置时，先定位实际解压路径。
