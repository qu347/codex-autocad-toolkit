---
name: autocad-mcp-review
description: Use when reviewing an AI-CAD MCP drawing in AutoCAD 2026, checking actual holes, bulged outlines, slots and native dimensions, or preparing a staged CAD teaching demonstration with evidence. Applies to geometric review and recording scripts, including the stepped flange case.
---
# AutoCAD MCP 复核与教学

以实际对象和测量值判断图纸是否完成。先取得当前图纸上下文、预期尺寸与生成批次的句柄；没有创建记录时用 `cad_get_entities` 查找对象并保留其图层和句柄。分页读取到 nextCursor 为空。

## 几何检查

用 `cad_get_geometry` 分批读取，每次最多 20 个唯一句柄。读取的是当前对象，不能用计划 JSON 代替执行证据。

调用参数和返回字段见 [读取契约](references/reading.md)。尺寸返回 measurement 和文本，不返回定义点；通过生成计划的语义 ID→句柄对应主要尺寸，未知尺寸的定义点另查 DXF 或在 CAD 中复核。

| 检查对象 | 必须读取或计算 |
|---|---|
| 外轮廓 | Polyline 的 closed、顶点、bulge；直线/圆弧衔接，实际包围范围 |
| 孔 | HOLE 层 Circle 的中心和 radius；节圆属于 CENTER 层，不计入孔 |
| 长圆槽 | 闭合多段线、半圆 bulge、圆弧半径与总长/总宽 |
| 尺寸 | 实体类型为原生 Dimension，measurement 匹配定义点和目标尺寸 |
| 图层 | 名称、线型、颜色、可见性与锁定状态 |

圆角半径计算：相邻顶点弦长 c、非零 bulge=b，则 `R=c*(1+b*b)/(4*abs(b))`。圆弧可能有极值点，不能只用顶点最小/最大值算整体包围框。半圆槽用端圆弧半径和直段长度还原总长。

默认阶梯法兰的坐标/检查值见 [案例检查表](references/flange-review.md)。数值容差默认 1e-6 个图纸单位用于本案例；这只是计算复核，不替代图纸上的制造公差。原生尺寸按坐标定义，不承诺修改原几何后自动联动。

原始实体总数仅用于辅助查重复。12 个 Circle 可以包含 11 个孔和 1 个节圆；25 个 Dimension 仍需逐项核实主要 measurement。检查预览中的标注文字是否重叠、符号是否重复或缺字。

## 发现问题

报告「对象句柄 → 预期 → 实际 → 修正范围」。先区分计划错误、连接失败、对象被用户改过和预览字体缺字。复核请求本身不授权修改已有用户对象。

用户已授权修正本次生成图形时，核实创建批次和当前对象。`cad_undo_batch` 只撤销允许撤销且未被改动的最近批次；拒绝时读取实际证据，不绕过检查删除其他图形。四孔模板的孔径 propose/apply 只用于对应模板，不适用于通用二维批次。

修正后使用新 GUID 重建受影响的阶段，重新测量。保留更早的正确阶段，不能为了演示速度复制重画到同一位置。

## 教学演示

针对当前案例写三栏脚本：「讲什么 → 屏幕演示什么 → 用什么证据说明」。先展示完成图吸引观众，再准备环境，按轮廓、孔槽、标注逐步演示，最后读取实际测量并导出文件。

每段只讲当前能验证的能力。用户要停顿拍摄时在阶段之间停，不在一次事务内部猜测完成进度。缩放、切换窗口等画面动作交给演示者；当前 MCP 不包含截图或视图控制接口。

保存使用新绝对路径；保存前读最新指纹。文件字节数大于零后可报告已导出，重新打开后再读取实体和尺寸才算验证重开。向观众说明工具负责执行、Skills 负责拆分和检查，用当前实测结果展示。
