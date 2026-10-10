# 验证记录

验证日期：2026-10-10。测试对象为本仓库独立目录内的源码与脚本。

## 验证环境

- Windows；AutoCAD 2026 核心引擎 R25.1。
- .NET SDK 8.0.425；Release 构建。
- 通过 AUTOCAD_PATH 指定实际安装目录，没有依赖原项目目录。

## 实际结果

| 检查 | 结果 |
|---|---|
| 编译 | 0 个警告，0 个错误 |
| AiCad.Contracts 单元测试 | 44 / 44 通过 |
| AiCad.Mechanical 单元测试 | 8 / 8 通过 |
| AiCad.Bridge 单元测试 | 34 / 34 通过 |
| 发布包检查 | AICAD_PACKAGE_VALID |
| 原生插件加载和状态 | AICAD_READY，pipe=available |
| 圆角底板三维实体与二维三视图 | plate2d3d 通过 |
| 重复请求幂等性 | duplicate_request 通过 |
| 修改提议取消与应用 | approval_cancel、approval_apply 通过 |
| 批次撤销与原生撤销组 | batch_undo、native_undo_group 通过 |
| 当前用户命名管道端到端通信 | pipe_end_to_end 通过 |
| Codex Skill 格式 | quick_validate.py 通过 |

共 86 项单元测试通过，另有上述 AutoCAD 原生集成检查。
集成测试仅临时把本地插件目录加入 TRUSTEDPATHS，加载后恢复；没有关闭 SECURELOAD。

## 阶梯法兰案例

默认参数的参考 DWG/DXF 和分阶段 AutoLISP 脚本见 examples/stepped-flange/。
图纸已在 AutoCAD 2026 生成、保存重开并校核：外形 220 × 160 mm、20 处 R8 圆角、中心孔 φ60、节圆 φ90 上 6 个 φ10 孔、4 个 φ9 安装孔、2 个 30 × 12 长圆槽，以及 25 个原生尺寸实体。
具体校核数值见该目录的 verification.json。

## AutoLISP 自动执行现场验证

2026-10-10 在 Windows 10 Pro 64位 10.0.19045、桌面 AutoCAD 2026 R25.1.60.0.0（ACADVER 25.1s）上实际执行了新的 `practice/flange_demo.lsp`。由原生 `/b` 入口自动运行三个阶段，分别导出预览、保存新的 DWG/DXF；未使用 Computer Use，未安装 MCP 插件。

- 三阶段均完成：OUTLINE 1个对象，HOLE 13个对象（11圆孔和2闭合槽），CENTER 27个对象，DIM 25个原生尺寸，TEXT 4个对象，共70个模型空间对象。
- 实际40个圆角切点与20个理论拐点逐项匹配；外形包络220×160 mm，20处R8，孔位、孔径、槽轮廓以及25个尺寸测量值符合默认提示词，单位mm、世界坐标系、几何Z=0。
- 重复运行三个阶段跳过，实体数量不变。已有输出路径被拒绝，DWG哈希不变。
- 保存后的DWG在 AutoCAD 2026 原生核心引擎中重新打开、导出并比较；AUDIT发现0个错误。
- `examples/stepped-flange/` 的8个参考文件哈希未改变。公开的实际图纸、阶段预览和校核记录见 `practice/`。
- 改参数练习只改变中央孔直径50、节圆直径100，同样实际完成自动三阶段、保存和原生重开校核；首孔圆心(160,80)，尺寸测量值50／100，原生AUDIT发现0个错误。练习脚本为 `practice/flange_practice.lsp`，结果为 `practice/flange_bore50_pcd100.dwg`、DXF及预览。

## 验证范围

本记录说明列出的版本、默认参数和测试路径已经实际执行。其他 AutoCAD 版本、自定义参数、桌面操作及具体工程任务应重新验证。
插件直接生成圆角矩形四孔底板；阶梯法兰由独立 AutoLISP 案例脚本绘制。
