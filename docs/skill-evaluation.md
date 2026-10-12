# Skills 应用测试

日期：2026-10-12。使用独立只读评测任务；禁止真实绘图、修改配置和文件。绘图代码在主会话开发与测试。

## 绘图参考技能

无Skill基线：能推理圆心、半径、GUID和指纹用途，但工具元数据没有完整计划字段/单位契约，不能可靠给出可执行planJson。

加入autocad-mcp-drawing后：给定指纹fixture-20261012-abc、单位Millimeter和中心(110,80)、直径60，输出合法2d-1.0计划，HOLE层、半径30、unit=millimeter均正确。主会话用真实Drawing2dPlan.Parse解析该输出，得到SKILL_GENERATED_PLAN_VALID。

超时变体：面对“换新GUID重画”的催促，下一步仍调用原requestId的cad_get_request_status，保存原完整payload，completed时读原句柄，不另造重复请求。此流程在真实MCP幂等测试中验证。

## 复核参考技能

无Skill基线已知道实体数量不足以证明几何正确。新增Skill主要补上实际工具契约、默认法兰坐标和bulge计算参考。

加入autocad-mcp-review后：面对12圆、3多段线、25尺寸的统计，未把它当作11孔与20个R8的验证。按图层和几何区分节圆；逐段用R=c*(1+b*b)/(4*abs(b))核对；槽按18+6+6核对总长30，宽度12。

第一轮指出读取参数/data字段不完整、安装脚本尚缺。补齐reading.md与plan-schema.md字段，并新增安装脚本后复测：给出可调用layout=Model/cursor=null参数，使用实际min/max而非顶点极值，明确尺寸查询不返回定义点或最终展开文本。路径与安装后Skill引用检查通过。

两Skill均通过quick_validate.py；agents/openai.yaml短描述长度、$skill-name默认提示词检查通过，安装副本与源码逐文件一致。

以上是应用参考测试，非模型每次调用都正确的保证。实际绘图仍必须以本机几何和文件结果为准。
