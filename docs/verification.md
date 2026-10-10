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

## 范围

本记录说明列出的版本、默认参数和测试路径已经实际执行。其他 AutoCAD 版本、自定义参数、桌面操作及具体工程任务应重新验证。
插件直接生成圆角矩形四孔底板；阶梯法兰由独立 AutoLISP 案例脚本绘制。
