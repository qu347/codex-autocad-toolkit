# MCP 读取契约

- `cad_get_context({})`：documentFingerprint/drawingName/unit/isReadOnly/currentUcs/selection。单位 Millimeter、Inch、DrawingUnit 或 null。
- `cad_get_layouts({"cursor":null,"pageSize":200})`：items 内 name/isModelSpace/entityCount；其他布局使用实际 name。
- `cad_get_entities({"layout":"Model","cursor":null,"pageSize":200})`：items 内 handle/entityType/layer/min/max，边界点 x/y/z，读取失败可能为 null。
- `cad_get_dimensions({"layout":"Model","cursor":null,"pageSize":200})`：handle/dimensionType/layer/measurement/displayText/textPosition。displayText 是用户覆盖模板（可能空或含 <>），不是渲染出的最终字串。
- `cad_get_layers({"cursor":null,"pageSize":200})`：name/color/lineType/isLocked/isOff/isFrozen。

以上分页输出均含 items/nextCursor/totalCount/documentFingerprint。pageSize 1–500，默认200；cursor 为 null 或服务器返回的字符串。Model 是模型空间；不凭中文界面猜布局名称。读取过程中指纹变化时检查图纸是否切换或修改，重新建立同一版本的证据。

`cad_get_geometry({"handlesJson":"[\"2CF\",\"2D9\"]"})` 每次最多20个唯一句柄，输出 items 和 documentFingerprint。每项有 handle/entityType/layer/data：

| 类型 | data 字段 |
|---|---|
| Line | start/end（x/y/z） |
| Circle | center/normal（x/y/z）、radius |
| Arc | center、radius、startAngleDegrees/endAngleDegrees、start/end、normal |
| Polyline | closed、elevation、normal、vertices（x/y/bulge） |
| AlignedDimension/RotatedDimension/RadialDimension/DiametricDimension | measurement、dimensionText、textPosition、dimensionType |

这些工具没有尺寸定义点查询、截图、任意命令或视图缩放能力。定义点从该次创建计划对应句柄或导出 DXF 获取；完整原生标注的显示字符及字体通过预览/CAD 检查。
