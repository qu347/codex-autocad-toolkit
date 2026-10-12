---
name: autocad-mcp-drawing
description: Use when Codex needs to connect to the AI-CAD MCP for Windows AutoCAD 2026 or draw staged 2D lines, circles, arcs, bulged polylines and native dimensions from explicit engineering dimensions. Includes connection checks, the 2d-1.0 plan schema, retry handling and DWG/DXF copy export.
---
# AutoCAD MCP 二维绘图

把明确的尺寸转换成结构化计划，在真实 AutoCAD 中分阶段创建，并读取实际结果。MCP 服务名通常为 `ai_cad`；使用当前会话发现的工具，名称前缀可能由客户端添加。

## 连接与尺寸

1. 调用 `cad_health`，再调用 `cad_get_context`。记录插件版本、图纸名称、单位、只读状态与 `documentFingerprint`。配置文件存在不代表连接成功。
2. 新接口需要 `cad_draw_2d`、`cad_get_layers`、`cad_get_geometry`、`cad_save_drawing`。缺少时按 [连接参考](references/connection.md) 更新插件并刷新 MCP；不要换成不支持此图形的四孔底板模板。
3. 优先在新建的公制空白图纸或用户指定的副本上演示。读取实际单位；`Millimeter` 对应计划 `millimeter`，`Inch` 对应 `inch`。单位未定义时先由用户在图纸中明确单位，再读上下文；不能默默把已有图纸当毫米。
4. 确定总尺寸、原点、孔径/中心坐标、圆角、槽尺寸和标注要求。缺失且影响结果的尺寸需要补齐。用户已给完整尺寸或同意默认案例时直接继续。

## 创建计划

编写 `planJson` 前读取 [完整字段与实例](references/plan-schema.md)。这是一个 JSON **字符串**，不是脚本；不传 Python、AutoLISP、CAD 命令或未经定义的字段。

- 坐标为 WCS XY，Z 固定为 0。点写成 `{ "x": 0, "y": 0 }`。
- 圆用半径；圆弧起止角是 0–360 范围内的度数，沿逆时针从起点到终点，可跨 0 度。
- 多段线每个顶点的 `bulge` 控制从该顶点到下一顶点的圆弧；闭合末段连接首顶点，不重复首点。`bulge=tan(夹角/4)`，正值逆时针；半圆为 ±1。
- 每次最多 500 实体、32 图层、每条多段线最多 256 顶点。实体 `id` 本阶段唯一，图层必须已存在或在 `layers` 中定义。
- 常用层：OUTLINE/HOLE 为 Continuous，CENTER 为 CENTER，DIM 为 Continuous。先读 `cad_get_layers`；同名层设置冲突时保留原层，选新名字或向用户明确冲突。
- 尺寸使用原生四类 dimension；`prefix` 只填 `6X `、`PCD ` 等说明。半径/直径符号由 AutoCAD 添加，不再填写 R 或 %%c。`<>` 由工具保留实测数值。

## 分阶段与重试

复杂图形通常分为「轮廓 → 孔槽/中心线 → 尺寸」，每阶段执行流程如下：

1. 读取当前 `cad_get_context`；为**新意图**生成新 GUID，将原样指纹与匹配单位写入计划。
2. 保存本次完整计划，然后调用 `cad_draw_2d(planJson)` 一次。记录 `requestId`、`batchId`、语义 ID 到句柄的映射。
3. 使用 `cad_get_geometry(handlesJson)`，每次最多 20 个句柄，读取实际几何。通过后再进入下一阶段；新阶段重新读当前指纹。

如果调用超时，先用**原 requestId** 调用 `cad_get_request_status`。状态 completed 时读取原句柄；结果未知时保留原完整 payload，在同一图纸核实状态后仅重发相同计划。不能为了重试换新 GUID，也不能把原请求的指纹换成新指纹。相同请求同计划不会重复创建；同 GUID 改内容会被拒绝。

状态 undone 时不重复旧请求；用户要求重新绘制属于新意图，读取上下文并使用新 GUID。遇到 TARGET_CHANGED 时先读取实际对象，不删除或覆盖被用户修改的对象。

## 复核与保存

- 检查闭合轮廓、实际孔中心/半径、槽长宽和原生尺寸 measurement。实体数量是辅助证据；CENTER 节圆不是孔。完整复核可使用 `$autocad-mcp-review`。
- 保存前重新读上下文。输出目录先创建，`cad_save_drawing` 使用新绝对 `.dwg`/`.dxf` 路径；现有文件与源图不会覆盖。
- 保存返回实际路径和字节数后，才报告已导出。超时先检查输出文件，避免盲目重试；可由用户打开 DWG，重新读对象确认内容。
- 报告具体结果与证据位置。未执行过的界面操作、版本、形状或尺寸变体不算已验证。
