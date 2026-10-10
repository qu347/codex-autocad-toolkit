# AI-CAD 插件

提供 AutoCAD 2026 .NET 插件、MCP Bridge、强类型计划、绘图证据读取和测试。

源码项目：
- AiCad.Contracts：计划、协议和校验。
- AiCad.Mechanical：圆角四孔底板参数与计划。
- AiCad.AutoCAD：三维实体、二维三视图、尺寸、命名管道服务及图纸证据读取。
- AiCad.Bridge：Codex MCP 入口，共13个 cad_* 工具。

安装路径通过 AUTOCAD_PATH 指定；默认 C:\Program Files\Autodesk\AutoCAD 2026。Autodesk SDK 程序集仅从本机引用，不随仓库分发。
接入方式见 [完整接入说明](../docs/getting-started.md)。

修改既有图形保留原插件的提议、一次性确认令牌、过期与对象校验。重复请求不重复创建对象。
