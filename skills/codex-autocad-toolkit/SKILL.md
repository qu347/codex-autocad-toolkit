---
name: codex-autocad-toolkit
description: 在本工具包中将明确的二维绘图提示词转为 AutoLISP，由 Codex 直接驱动 Windows AutoCAD 分阶段显示、保存和校核；也支持 AI-CAD MCP 的圆角四孔底板。
---
# Codex AutoCAD 工具包

此技能与当前工具包仓库配套使用。README、docs、examples、practice 路径以仓库根目录为基准；scripts 路径相对本技能目录。

- 先读取 README、`docs/getting-started.md` 与相关案例，确认实际 Windows、AutoCAD 版本。AutoLISP 路线不安装 MCP 插件，不需要 .NET SDK。仅在用户选择 MCP 路线时检查 .NET 8 SDK，并实际调用 `cad_health`、`cad_get_context` 验证连接。
- 圆角矩形四孔底板用 `cad_create_rounded_plate`。必须取得宽、深、厚、圆角、孔径和两方向孔边距的绝对数值；先检查当前图纸单位和只读状态。按工具定义使用同一 requestId，超时先查询状态。
- 读取图纸可用 layouts、entities、dimensions、text/tables、blocks 的分页工具。读到 nextCursor 时继续分页。
- 修改插件生成的孔径使用 propose/apply 流程；显示修改摘要，遵守用户实际授权和插件的一次性令牌机制。不要绕过插件校验。
- 二维 AutoLISP 默认由 Codex 直接执行：用户提交尺寸提示词后，简述计划、检查尺寸和间隔，在 `practice/` 创建本次脚本，再自动绘制、保存新 DWG 并返回实际路径。用户不用 APPLOAD 或输入命令；只有用户明确要求手动教学时，才交付手动加载和阶段命令。尺寸冲突要指出位置，不能自行改尺寸。
- 分阶段可视化使用 AutoCAD 自身的命令、REGEN 和 ZOOM；每阶段显示结果、校核对象，并留出观看时间。不要使用 Computer Use、鼠标键盘模拟或界面截图。通过原生 BMP 导出阶段预览；把实际运行检查与代码检查区分开。
- 阶梯法兰参考 `examples/stepped-flange/flange_demo.lsp`，本次脚本放 `practice/flange_demo.lsp`。在新空白图纸自动依次执行 CADOUTLINE、CADHOLES、CADDIMS。可由 Codex 执行 [scripts/run-visible-lisp.py](scripts/run-visible-lisp.py)：传入实际 acad.exe、acadiso.dwt、本次 LSP 与未占用的输出 DWG 路径。它通过 AutoCAD 原生 `/b` 脚本入口执行，导出三阶段预览、DWG、DXF 和运行记录，不需要 MCP 或 Computer Use。这个入口只针对阶梯法兰的三个阶段；其他形状应根据提示词编写并校核自己的 AutoLISP，不能套用法兰几何或圆角底板 MCP。
- 自动执行只能创建新图纸和新文件，保留 `examples/` 的参考脚本与原图。阶段完成后校核，再开始下一阶段；失败时保留运行记录并停止，不盲目重跑、不删除已有对象。实际尺寸对象、闭合轮廓、孔槽及圆角必须从生成结果读取。保存后用原生 AutoCAD 引擎重开校核，并保留最终图纸供用户查看。
- 改 LSP 参数后重新生成并复核，不宣称旧图会自动关联更新。检查闭合轮廓、孔槽位置、圆角、原生尺寸、重复对象和保存重开结果。
- 默认参数已验证的证据见案例 verification.json；其他参数、其他软件版本与桌面 UI 流程需要按实际结果另行验证。
