# 2d-1.0 字段契约

`cad_draw_2d` 参数只有 `planJson` 字符串；内容严格区分字段大小写，拒绝未知字段。所有实体的 `type` 放在对象第一项。

完整圆示例：先将指纹换成当前工具返回值，并为新意图生成 GUID。

```json
{
  "schemaVersion": "2d-1.0",
  "requestId": "22506c80-d656-4f97-9b04-0b64b50e7766",
  "documentFingerprint": "REPLACE_WITH_CURRENT_CONTEXT",
  "unit": "millimeter",
  "layers": [{"name": "HOLE", "color": 7, "lineType": "Continuous"}],
  "entities": [{"type": "circle", "id": "bore", "layer": "HOLE", "center": {"x": 110, "y": 80}, "radius": 30}]
}
```

顶层六字段全部必填。`layers` 可为空数组（复用已存在的层），`entities` 可为空数组（只建层），二者不可同时为空。单位字符串：`millimeter`、`inch`、`drawingUnit`，必须匹配 context。其他已定义 CAD 单位会返回 DrawingUnit；它不代表毫米或自动换算，应先明确该图纸一个单位的实际含义。默认教学用毫米。

图层字段：`name` 必填，`color` 默认 7，范围 1–255；`lineType` 默认 Continuous，允许 Continuous/CENTER/HIDDEN。实体共同字段：`type`、`id`、`layer`。点均为 x/y 两字段。坐标有限且绝对值不大于 1e9；几何不退化。

| type | 必填几何字段 | 可选字段 |
|---|---|---|
| line | start 点、end 点（不同） | 无 |
| circle | center 点、radius > 0 | 无 |
| arc | center、radius、startAngleDegrees、endAngleDegrees | 无；角度均 ≥0 且 <360，起止不同 |
| polyline | vertices 数组、closed 布尔值 | 顶点 bulge 默认 0；顶点 x/y 必填 |
| aligned_dimension | first、second、linePoint（三点不共线） | prefix 默认空 |
| linear_dimension | first、second、linePoint | rotationDegrees 默认 0，只可 0/90；prefix |
| radial_dimension | center、chord（圆上点） | leaderLength 默认 10 且 >0；prefix；textPosition 点 |
| diameter_dimension | chord、farChord（直径两个端点） | leaderLength 默认 10 且 >0；prefix；textPosition 点 |

原生尺寸按这些定义点测量，并非通过句柄关联原轮廓；几何改变后需要同步更新尺寸并复核，不承诺自动联动。径向 textPosition 可省略；指定时影响文字位置，不能改变实测值。

长圆槽示例（30×12，中心 25,80，孔槽层）：

```json
{"type":"polyline","id":"slot-left","layer":"HOLE","closed":true,"vertices":[{"x":16,"y":74,"bulge":0},{"x":34,"y":74,"bulge":1},{"x":34,"y":86,"bulge":0},{"x":16,"y":86,"bulge":1}]}
```

水平尺寸示例：

```json
{"type":"linear_dimension","id":"width","layer":"DIM","first":{"x":0,"y":80},"second":{"x":220,"y":80},"linePoint":{"x":110,"y":-50},"rotationDegrees":0}
```

读取：`cad_get_geometry({"handlesJson":"[\"1A\",\"1B\"]"})` 返回 items 的 entityType、layer、data；每次 1–20 个唯一十六进制句柄。图层分页参数 `cursor`/`pageSize`，看到 nextCursor 继续。

需要发现现有对象时：`cad_get_entities({"layout":"Model","cursor":null,"pageSize":200})`。其他布局先调用 `cad_get_layouts({"cursor":null})`，使用返回的实际 name。分页统一返回 items/nextCursor/totalCount/documentFingerprint；实体每项含 handle/entityType/layer/min/max。min/max 是 x/y/z 点或 null，不包含尺寸块里的子对象。

几何 data 的实际字段（不是猜测的 CAD 属性）：

| entityType | data 字段 |
|---|---|
| Line | start、end（x/y/z 点） |
| Circle | center（x/y/z）、radius、normal（x/y/z） |
| Arc | center、radius、startAngleDegrees、endAngleDegrees、start、end、normal |
| Polyline | closed、elevation、normal、vertices（x/y/bulge 数组） |
| 各类 Dimension | measurement、dimensionText、textPosition、dimensionType |

尺寸几何读取不返回 first/second 等定义点。使用创建计划和语义 ID→句柄映射对应尺寸，读取 measurement 核对；既有未知尺寸的定义点需要检查导出的 DXF 或在 CAD 中人工复核。`cad_get_dimensions({"layout":"Model","cursor":null})` 可一次分页列出 handle/dimensionType/layer/measurement/displayText/textPosition，displayText 是原 DimensionText 模板，并非展开后的最终显示字符串。

保存：`cad_save_drawing({"documentFingerprint":"当前指纹","dwgPath":"C:\\work\\new.dwg","dxfPath":"C:\\work\\new.dxf"})`，输出目录必须存在，两路径至少给一个。状态查询：`cad_get_request_status({"requestId":"原GUID"})`。
