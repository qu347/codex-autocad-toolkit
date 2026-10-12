"""Independently check an AutoCAD-exported teaching DXF and render its entities."""
import argparse
from collections import Counter
import json
import math
from pathlib import Path
import ezdxf
from ezdxf import bbox
from ezdxf.math import bulge_to_arc
from ezdxf.addons.drawing import Frontend, RenderContext
from ezdxf.addons.drawing.matplotlib import MatplotlibBackend
from ezdxf.addons.drawing.config import Configuration, BackgroundPolicy, ColorPolicy
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def verify(path):
    doc = ezdxf.readfile(path)
    model = doc.modelspace()
    audit = doc.audit()
    assert doc.units == 4 and not audit.errors and not audit.fixes
    assert len(model) == 66
    outlines = list(model.query('LWPOLYLINE[layer=="OUTLINE"]'))
    assert len(outlines) == 1 and outlines[0].closed
    extents = bbox.extents(outlines)
    assert extents.extmin.isclose((0, 0, 0)) and extents.extmax.isclose((220, 160, 0))
    vertices = list(outlines[0].get_points("xyb"))
    assert len(vertices) == 40
    arcs = []
    for i, (x, y, bulge) in enumerate(vertices):
        if bulge:
            nx, ny, _ = vertices[(i + 1) % len(vertices)]
            _, _, _, radius = bulge_to_arc((x, y), (nx, ny), bulge)
            assert abs(radius - 8) < 1e-6
            arcs.append(radius)
    assert len(arcs) == 20
    holes = list(model.query('CIRCLE[layer=="HOLE"]'))
    expected = [(110, 80, 30), (155, 80, 5), (132.5, 118.971143, 5), (87.5, 118.971143, 5),
                (65, 80, 5), (87.5, 41.028857, 5), (132.5, 41.028857, 5),
                (45, 40, 4.5), (175, 40, 4.5), (45, 120, 4.5), (175, 120, 4.5)]
    actual = sorted((round(e.dxf.center.x, 6), round(e.dxf.center.y, 6), round(e.dxf.radius, 6)) for e in holes)
    assert actual == sorted(expected)
    for i, first in enumerate(holes):
        for second in holes[i + 1:]:
            assert first.dxf.center.distance(second.dxf.center) > first.dxf.radius + second.dxf.radius
    slots = list(model.query('LWPOLYLINE[layer=="HOLE"]'))
    assert len(slots) == 2 and all(e.closed for e in slots)
    bounds = []
    for slot in slots:
        extents = bbox.extents([slot])
        assert abs(extents.size.x - 30) < 1e-6 and abs(extents.size.y - 12) < 1e-6
        bounds.append(tuple(round(n, 6) for n in (extents.extmin.x, extents.extmin.y, extents.extmax.x, extents.extmax.y)))
    assert sorted(bounds) == [(10, 74, 40, 86), (180, 74, 210, 86)]
    dimensions = list(model.query("DIMENSION"))
    assert len(dimensions) == 25 and all(e.dxf.geometry in doc.blocks for e in dimensions)
    # AutoCAD adds R/diameter symbols around <> itself, including prefixes.
    for dimension in dimensions:
        texts = [e.text for e in doc.blocks[dimension.dxf.geometry] if e.dxftype() == "MTEXT"]
        assert all("RR" not in text and "%%c" not in text for text in texts), texts
    assert doc.styles.has_entry("AICAD-2D-DIM")
    assert doc.styles.get("AICAD-2D-DIM").dxf.font.lower() == "seguisym.ttf"
    report = {"units": "mm", "modelspaceEntities": len(model), "entityTypes": dict(Counter(e.dxftype() for e in model)),
              "outline": {"closed": True, "width": 220, "height": 160, "vertices": 40, "fillets": 20, "radius": 8},
              "holes": actual, "slots": bounds, "nativeDimensions": 25,
              "dxfAuditErrors": len(audit.errors), "dxfAuditFixes": len(audit.fixes)}
    return doc, report


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("dxf", type=Path)
    parser.add_argument("--preview", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    drawing, report = verify(args.dxf)
    fig = plt.figure(figsize=(12, 9), dpi=180)
    ax = fig.add_axes([0.02, 0.02, 0.96, 0.96])
    ax.set_aspect("equal")
    config = Configuration(background_policy=BackgroundPolicy.WHITE, color_policy=ColorPolicy.BLACK)
    Frontend(RenderContext(drawing), MatplotlibBackend(ax), config=config).draw_layout(drawing.modelspace(), finalize=True)
    ax.set_axis_off()
    fig.savefig(args.preview, dpi=180, facecolor="white")
    plt.close(fig)
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("AICAD_DXF_VALID", report["modelspaceEntities"], "entities;", report["nativeDimensions"], "native dimensions")
