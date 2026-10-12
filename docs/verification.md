# 1.1.0 验证记录

日期：2026-10-12。测试对象为此工具包的本地源码、编译包、安装后的插件与Skills。

## 环境与执行范围

Windows，AutoCAD 2026核心引擎R25.1（W.60.0.0），.NET SDK8.0.425，Release，Python3.12。MCP调用使用真实JSON-RPC STDIO客户端，再经当前用户命名管道到AutoCAD主线程与数据库事务。Core Console 测试通过 AICADWAITFORRPC 泵送主线程；这不是桌面录屏流程的验证。

## 结果

| 检查 | 实际结果 |
|---|---|
| 编译 | 0警告、0错误 |
| Contracts 单元测试 | 67/67 |
| Mechanical 单元测试 | 8/8 |
| Bridge 单元测试 | 38/38 |
| 总计 | 113项通过，保留原有86项 |
| 发布包 | AICAD_PACKAGE_VALID，不含Autodesk运行时DLL |
| 新MCP | initialize、17工具列表、known error返回通过 |
| 原生插件与旧模板 | AICAD_READY；plate2d3d、approval_cancel/apply、duplicate_request、batch_undo、native_undo_group、pipe_end_to_end通过 |
| 二维基础实体 | Line、Circle、Arc、bulge Polyline、AlignedDimension实创建与读回通过 |
| 复杂阶梯法兰 | 三阶段创建66对象，25原生尺寸，主要measurement匹配 |
| 幂等与保护 | 同请求同句柄；同GUID改计划被拒绝；已有输出不覆盖；图层冲突回滚通过 |
| 被改对象保护 | 修改圆半径后撤销拒绝；修改多段线宽度后撤销和重试拒绝；恢复后正常撤销 |
| DWG重开 | 66对象、25尺寸；持久化原请求再次返回原句柄 |
| 独立DXF | 无审计错误/修复；闭合轮廓、20处R8、11孔与两槽核验通过 |
| 成图视觉 | 实际DXF渲染PNG已查看，径向符号无重复或缺字 |
| Skills | 两套quick_validate、UI元数据与安装副本一致性通过；独立只读应用测试通过 |
| 本机安装 | Native/Bridge与发布包一致；安装后17工具、实际圆绘制和DWG保存通过 |

真实法兰证据在 [mcp-evidence.json](../examples/mcp-stepped-flange/mcp-evidence.json)、[reopen-evidence.json](../examples/mcp-stepped-flange/reopen-evidence.json) 与 [verification.json](../examples/mcp-stepped-flange/verification.json)。记录的路径、进程号、指纹为当次测试值，不能当作下次连接上下文。

## 修正与回归

- 原生尺寸自动添加R/直径符号；自定义数量前缀只保留前缀与<>，修正重复RR与%%c。独立DXF校核先复现失败，再修正通过。
- acadiso模板Arial字体没有U+2205字形。新增独立AICAD-2D-DIM样式，使用本机Windows的seguisym.ttf，不修改Standard样式；实际导出预览直径符号可见。
- 尺寸文字重生成产生约1e-13数值漂移。快照文字位置取6位小数，重复请求/保存重开稳定，实际几何修改仍被拒绝。
- 审查发现多段线宽度修改遗漏。真实旧代码撤销误通过；补充各段起止宽度、法向与厚度后，真实MCP撤销/重试返回TARGET_CHANGED。只补新二维快照，保留旧模板快照格式。

## 范围

默认法兰：220×160 mm，20处R8，中央Ø60，Ø90节圆上6×Ø10，4×Ø9，两处30×12槽。新MCP绘图见examples/mcp-stepped-flange；原有独立LSP案例保留在examples/stepped-flange。其他软件版本、尺寸变体和具体制造任务需按当次结果复核。

原生尺寸依赖定义点，未实现对轮廓句柄的关联更新；图形修改后重新复核尺寸。当前MCP不含截图和缩放接口。测试加载仅临时添加本地插件目录到TRUSTEDPATHS，随后恢复，没有关闭SECURELOAD。

更新插件、配置与Skills已保留备份；本会话旧MCP进程不会自动重新加载新工具目录，需要刷新MCP或新开Codex会话后使用。

发布ZIP已逐文件SHA-256核验；解压后使用Windows PowerShell运行编译包安装与Skills安装/备份，得到EXTRACTED_PACKAGE_INSTALL_VALID。源码留在feat/autocad-mcp-2d分支。
