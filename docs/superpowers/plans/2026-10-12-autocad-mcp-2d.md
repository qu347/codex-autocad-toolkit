# AutoCAD MCP 二维绘图与教学 Skills 实现计划

> **给 agentic 执行者：** 使用 superpowers:executing-plans 在当前会话逐任务执行。用户已经要求“编写吧”；主体开发在当前会话，不另行询问执行方式；Skills应用测试与代码审查使用独立只读评测任务。

**目标：** 完成结构化二维绘图 MCP、配套 Skills 和真实阶梯法兰验证交付。
**架构：** 保留 STDIO Bridge、当前用户命名管道和 AutoCAD 主线程队列。增加独立二维计划校验、原生执行器和几何/保存服务，复用请求状态与批次记录。
**技术栈：** C#、.NET 8、ModelContextProtocol 2.2.0、AutoCAD 2026 Managed API、xUnit；教学 JSON 和 DXF 核验使用 Python。
**Spec：** [设计文件](../specs/2026-10-12-autocad-mcp-2d-design.md)

## 全局约束

- Windows、AutoCAD 2026（R25.1）、.NET 8。
- WCS 二维坐标，Z=0；单位必须与当前图纸一致。
- 每次计划最多 500 个实体、32 个图层，每条多段线最多 256 个顶点。
- MCP 不开放任意代码执行；保存只创建新文件，不覆盖已有路径或源文件。
- 保留现有功能与 86 项测试；本次不制作最终视频或新增企业部署功能。
- 在已有独立工具包目录中新建开发分支，保留原始 3d 项目。

## 任务 1：结构化计划与 MCP 工具

**文件：** 新建 `plugin/src/AiCad.Contracts/Drawing2d.cs`、`Drawing2dValidator.cs`、`DrawingFiles.cs`、`plugin/src/AiCad.Bridge/Cad2dTools.cs`；新增 `plugin/tests/AiCad.Contracts.Tests/Drawing2dTests.cs` 和 `plugin/tests/AiCad.Bridge.Tests/Cad2dToolsTests.cs`。仅为明确 MCP 错误修改现有 `CadToolException` 的基类。

**接口：** `Drawing2dPlan.Parse(string)` 解析并校验 JSON，返回二维计划；`Drawing2dValidator.RequireContext(plan, context)` 校验单位、只读和指纹；`DrawingFilePolicy.Validate(request, sourcePath)` 校验新输出路径。Bridge 工具为 `Draw2dAsync(planJson)`、`GetLayersAsync(cursor,pageSize)`、`GetGeometryAsync(handlesJson)`、`SaveDrawingAsync(documentFingerprint,dwgPath,dxfPath)`，消费 `ICadClient`。

- [x] RED：先通过反射调用尚不存在的解析入口，让断言因缺少功能失败，而非编译失败。

```csharp
var type = typeof(PlanJson).Assembly.GetType("AiCad.Contracts.Drawing2dPlan");
Assert.NotNull(type);
var parse = type.GetMethod("Parse", new[] { typeof(string) });
Assert.NotNull(parse);
var plan = parse.Invoke(null, new object[] { validJson });
Assert.NotNull(plan);
```

- [x] 增加圆半径为零、重合线端点、重复 ID、未知实体类型、非有限数、图层/顶点超限、单位与指纹不匹配，以及已有文件/错误扩展名的拒绝测试。Bridge 测试断言非法计划不发送写 RPC，合法计划只发送一次目标 RPC。
- [x] GREEN：实现 DTO、严格 JSON 解析、校验和四个 MCP 工具。已知错误通过 `McpException` 显示代码与可处理消息，日志保持 stderr。
- [x] 验证：`dotnet test plugin/AiCad.sln -c Release`，全部通过后提交本任务文件。

## 任务 2：AutoCAD 原生绘图、读取与保存

**文件：** 新建 `plugin/src/AiCad.AutoCAD/Drawing2dExecutor.cs` 和 `Drawing2dServices.cs`；修改 `PluginEntry.cs` 的四个 RPC 分派入口，以及 `AutoCadPlanExecutor.cs` 中供二维创建复用的快照/撤销范围和新类型快照分支。新增 `plugin/tests/AutoCAD.Integration/run-mcp-2d.py`。

**接口：** `Drawing2dExecutor.ExecuteCreate(Document, Drawing2dPlan)` 返回已有 `CreateResult` 并写入已有批次元数据；几何读取返回句柄与结构化几何；保存消费 `SaveDrawingRequest`、返回路径和字节数。现有 `get_request_status` 继续读取共享批次记录。

- [x] RED：用真实 MCP STDIO 客户端初始化现有 Bridge，断言工具列表包含四个新增工具，并调用绘图；在 Native 实现前记录缺少能力或不支持 RPC 的失败。

```python
response = client.call("tools/list", {})
names = {tool["name"] for tool in response["result"]["tools"]}
assert {"cad_draw_2d", "cad_get_layers", "cad_get_geometry", "cad_save_drawing"} <= names
```

- [x] GREEN：在文档锁和事务中创建实体/图层。校验后再写，失败回滚；同请求不同计划拒绝，相同请求返回原批次。支持 Line/Circle/Arc/Polyline 和四类原生尺寸，角度从度转弧度。
- [x] 保留已有图层；冲突、锁定图层、缺少线型、无活动图纸返回明确错误。新增实体类型的快照覆盖其关键几何属性；批次撤销仍只针对最近未被更改的 AI 实体。
- [x] 保存通过 AutoCAD `SaveAs`/`DxfOut` 生成临时文件再发布到新路径，避免覆盖和改名源文件；检查非空输出并清理本次临时文件。
- [x] 验证：本机 Core Console 加载插件，真实 MCP 客户端调用 health/context、分阶段绘图、读取、保存；再运行现有原生集成检查。提交通过验证的 Native 代码。

## 任务 3：教学案例、Skills、安装与交付

**文件：** 新建 `examples/mcp-stepped-flange/` 的三阶段 JSON、提示词、说明和验证记录；新增 `skills/autocad-mcp-drawing/`、`skills/autocad-mcp-review/`（各含 `SKILL.md`、`agents/openai.yaml` 和必要参考）；补充连接/Skills 安装脚本、根 README、接入和验证文档。

**接口：** 阶段 JSON 遵循 `Drawing2dPlan`；执行时只替换请求 GUID 和当前指纹。Skills 使用真实 MCP 名称，并通过原生几何、尺寸和保存证据判断完成。

- [x] 生成默认 220×160 mm、20×R8、Ø60 中央孔、Ø90 节圆 6×Ø10、4×Ø9、两处 30×12 长圆槽的阶段 JSON；使用新 MCP 生成 DWG/DXF。
- [x] 验证并保存阶段证据：闭合轮廓、圆角半径、孔中心/半径、槽长宽、原生尺寸；重试无重复实体，同 GUID 不同参数被拒绝。

```python
assert outline["closed"]
assert len(outline["vertices"]) == 40
assert len(actual_holes) == 11
assert all(abs(r - 8.0) < 1e-6 for r in outline_arc_radii)
assert all(entity.dxftype() == "DIMENSION" for entity in actual_dimensions)
```

- [x] 重新打开 DWG，再读 MCP 证据；DXF 数值校核并渲染 PNG，查看预览。缺陷需要先复现，再修正和复测。
- [x] 编写连接、阶段绘图和复核 Skills，运行 `skill-creator/scripts/quick_validate.py`。安装配置仅替换本工具包条目并备份原配置，不泄露凭据或会话令牌。
- [x] 完整构建与测试、包检查、实际 MCP 测试；按显式文件清单打包源码、可执行 Bridge、插件和 Skills，不加入 SDK/.git/bin/obj/无关内容。
- [x] 对照 Spec 检查全部交付项；提交验证记录和交付文件。留在开发分支，不自动推送或合并。
