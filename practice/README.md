# 本次 AutoLISP 自动绘图结果

此目录保留2026-10-10在 Windows AutoCAD 2026 中实际重新生成的教学结果，单位mm、模型空间1:1、世界坐标系、几何Z=0。原版脚本和参考图仍在 `examples/stepped-flange/`。

- [本次脚本](flange_demo.lsp)
- [DWG](flange_auto.dwg) · [DXF](flange_auto.dxf)
- [实际校核记录](verification.json)

由 Codex 自动依次执行 CADOUTLINE、CADHOLES、CADDIMS；无需手动输入命令。每阶段自动显示并停留3秒。

## 第一阶段 轮廓

![实际轮廓](flange_auto_stage1.png)

## 第二阶段 孔槽和中心线

![实际孔槽](flange_auto_stage2.png)

## 第三阶段 原生尺寸

![实际标注](flange_auto_stage3.png)

实测为20处R8、11个实际圆孔、2个闭合槽和25个原生尺寸。保存重开校核通过，原生AUDIT发现0个错误。辅助节圆属于CENTER层，不计为实际孔。

## 改参数练习 中央孔50 节圆100

仅将 `fd:bore-d` 改为50、`fd:pcd` 改为100，其余参数保持原值。实际自动生成并保存重开校核完成，右侧首孔圆心为(160,80)，25个原生尺寸读取到孔径50和节圆100，AUDIT发现0个错误。

- [练习脚本](flange_practice.lsp)
- [练习 DWG](flange_bore50_pcd100.dwg) · [练习 DXF](flange_bore50_pcd100.dxf)

![实际改参数图纸](flange_bore50_pcd100_stage3.png)
