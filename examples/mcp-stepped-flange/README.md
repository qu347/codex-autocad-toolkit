# 阶梯法兰安装板：真实 MCP 教学案例

![实际导出 DXF 预览](preview.png)

全部几何由 cad_draw_2d 在 AutoCAD 2026 原生事务中创建。

| 阶段 | 计划模板 | 新增对象 |
|---|---|---|
| 轮廓 | 01-outline.json | 1条闭合多段线，40顶点、20处R8 |
| 孔槽/中心线 | 02-holes.json | 40个对象，包含11个孔圆、2个槽、节圆和中心线 |
| 原生尺寸 | 03-dimensions.json | 25个 DIMENSION |

使用 [prompts.txt](prompts.txt)。计划必须在调用前填入最新指纹和新 GUID；重复调用应保留原始计划，不重新换 GUID。

外形220×160 mm，中央孔Ø60，Ø90节圆上6×Ø10，4×Ø9，两处30×12槽。详见 [复核检查表](../../skills/autocad-mcp-review/references/flange-review.md)。

- flange_mcp.dwg / flange_mcp.dxf：真实生成并保存的默认图纸。
- mcp-evidence.json：17工具列表、插件版本、阶段实际计划与句柄、几何、幂等/冲突/回滚/保存结果。
- reopen-evidence.json：DWG重新打开后的66对象、25尺寸和原请求幂等结果。
- verification.json：独立DXF几何和审计结果。
- preview.png：渲染实际 DXF，已检查直径/半径符号和标注位置。
- build-plans.py：重建模板；verify-dxf.py：独立复核并渲染，依赖ezdxf/matplotlib。

证据中的绝对路径、进程号和指纹属于当次测试，不能复制为用户当前上下文。尺寸文字用独立 AICAD-2D-DIM 样式（Windows seguisym.ttf），保留图纸已有样式。
