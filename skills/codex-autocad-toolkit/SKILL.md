---
name: codex-autocad-toolkit
description: 在本工具包中接入 Codex 与 Windows AutoCAD 2026，使用 AI-CAD MCP 创建圆角四孔底板或运行阶梯法兰 AutoLISP 教学案例，并检查实际图形和尺寸。
---
# Codex AutoCAD 工具包

此技能与当前工具包仓库配套使用。路径以仓库根目录为基准。

- 接入或环境检查时读取 `docs/getting-started.md`，确认 Windows、AutoCAD 2026 与插件路线所需的 .NET 8 SDK。不能只凭配置文件宣称连接成功，应实际调用 `cad_health` 和 `cad_get_context`。
- 圆角矩形四孔底板用 `cad_create_rounded_plate`。必须取得宽、深、厚、圆角、孔径和两方向孔边距的绝对数值；先检查当前图纸单位和只读状态。按工具定义使用同一 requestId，超时先查询状态。
- 读取图纸可用 layouts、entities、dimensions、text/tables、blocks 的分页工具。读到 nextCursor 时继续分页。
- 修改插件生成的孔径使用 propose/apply 流程；显示修改摘要，遵守用户实际授权和插件的一次性令牌机制。不要绕过插件校验。
- 阶梯法兰及三个演示命令使用 `examples/stepped-flange/` 中的独立 LSP，在新空白图纸内依次运行 CADOUTLINE、CADHOLES、CADDIMS。不要把它发送给不支持该形状的圆角底板 MCP 工具。
- 改 LSP 参数后重新生成并复核，不宣称旧图会自动关联更新。检查闭合轮廓、孔槽位置、圆角、原生尺寸、重复对象和保存重开结果。
- 默认参数已验证的证据见案例 verification.json；其他参数、其他软件版本与桌面 UI 流程需要按实际结果另行验证。
