"""Build the three documented, dimensioned flange teaching stages.

Produces structured MCP plans, not executable CAD code. Replace each stage's
documentFingerprint and requestId after reading the active drawing context.
"""
import json
import math
from pathlib import Path
import uuid


def p(x, y):
    return {"x": round(x, 12), "y": round(y, 12)}


def line(name, start, end, layer="CENTER"):
    return {"type": "line", "id": name, "layer": layer, "start": p(*start), "end": p(*end)}


def circle(name, center, radius, layer="HOLE"):
    return {"type": "circle", "id": name, "layer": layer, "center": p(*center), "radius": radius}


def linear(name, first, second, offset, vertical=False):
    position = (offset, (first[1] + second[1]) / 2) if vertical else ((first[0] + second[0]) / 2, offset)
    return {"type": "linear_dimension", "id": name, "layer": "DIM", "first": p(*first),
            "second": p(*second), "linePoint": p(*position), "rotationDegrees": 90 if vertical else 0}


def diameter(name, center, radius, position, prefix=""):
    angle = math.atan2(position[1] - center[1], position[0] - center[0])
    delta = (radius * math.cos(angle), radius * math.sin(angle))
    return {"type": "diameter_dimension", "id": name, "layer": "DIM",
            "chord": p(center[0] + delta[0], center[1] + delta[1]),
            "farChord": p(center[0] - delta[0], center[1] - delta[1]),
            "leaderLength": 10, "prefix": prefix, "textPosition": p(*position)}


def stages():
    corners = [(0, 50), (25, 50), (25, 20), (55, 20), (55, 0), (165, 0),
               (165, 20), (195, 20), (195, 50), (220, 50), (220, 110),
               (195, 110), (195, 140), (165, 140), (165, 160), (55, 160),
               (55, 140), (25, 140), (25, 110), (0, 110)]
    vertices = []
    for i, corner in enumerate(corners):
        before, after = corners[i - 1], corners[(i + 1) % len(corners)]
        incoming = (corner[0] - before[0], corner[1] - before[1])
        outgoing = (after[0] - corner[0], after[1] - corner[1])
        u = tuple(x / math.hypot(*incoming) for x in incoming)
        v = tuple(x / math.hypot(*outgoing) for x in outgoing)
        turn = u[0] * v[1] - u[1] * v[0]
        vertices.append({**p(corner[0] - u[0] * 8, corner[1] - u[1] * 8), "bulge": turn * math.tan(math.pi / 8)})
        vertices.append({**p(corner[0] + v[0] * 8, corner[1] + v[1] * 8), "bulge": 0})
    outline = [{"type": "polyline", "id": "flange-outline", "layer": "OUTLINE", "closed": True, "vertices": vertices}]
    holes = [circle("bore", (110, 80), 30), circle("pitch-circle", (110, 80), 45, "CENTER"),
             line("axis-x", (-5, 80), (225, 80)), line("axis-y", (110, -5), (110, 165))]

    def cross(name, center, size):
        holes.extend([line(name + "-x", (center[0] - size, center[1]), (center[0] + size, center[1])),
                      line(name + "-y", (center[0], center[1] - size), (center[0], center[1] + size))])

    for i in range(6):
        a = i * math.tau / 6
        center = (110 + 45 * math.cos(a), 80 + 45 * math.sin(a))
        holes.append(circle(f"bolt-{i + 1}", center, 5))
        cross(f"bolt-center-{i + 1}", center, 7)
    for i, center in enumerate([(45, 40), (175, 40), (45, 120), (175, 120)], 1):
        holes.append(circle(f"mount-{i}", center, 4.5))
        cross(f"mount-center-{i}", center, 6.5)
    for i, x in enumerate([25, 195], 1):
        holes.append({"type": "polyline", "id": f"slot-{i}", "layer": "HOLE", "closed": True,
                      "vertices": [{"x": x - 9, "y": 74, "bulge": 0}, {"x": x + 9, "y": 74, "bulge": 1},
                                   {"x": x + 9, "y": 86, "bulge": 0}, {"x": x - 9, "y": 86, "bulge": 1}]})
        cross(f"slot-center-{i}", (x, 80), 18)
    dimensions = [linear("width", (0, 80), (220, 80), -50),
                  linear("height", (110, 0), (110, 160), 270, True)]
    xs, ys = [0, 25, 55, 165, 195, 220], [110, 140, 160, 160, 140, 110]
    for i in range(5):
        dimensions.append(linear(f"step-x-{i + 1}", (xs[i], ys[i]), (xs[i + 1], ys[i + 1]), 183))
    xs, ys = [55, 25, 0, 0, 25, 55], [0, 20, 50, 110, 140, 160]
    for i in range(5):
        dimensions.append(linear(f"step-y-{i + 1}", (xs[i], ys[i]), (xs[i + 1], ys[i + 1]), -25, True))
    dimensions.extend([linear("mount-offset-x", (0, 50), (45, 40), -17),
                       linear("mount-spacing-x", (45, 40), (175, 40), -17),
                       linear("mount-offset-y", (55, 0), (45, 40), -45, True),
                       linear("mount-spacing-y", (45, 40), (45, 120), -45, True),
                       linear("slot-position-x", (0, 50), (25, 80), -33),
                       linear("slot-position-y", (55, 0), (25, 80), -65, True),
                       linear("slot-length", (10, 80), (40, 80), 99),
                       linear("slot-width", (25, 74), (25, 86), 52, True),
                       diameter("bore-diameter", (110, 80), 30, (103, 88)),
                       diameter("pitch-diameter", (110, 80), 45, (238, 33.4118100139), "PCD "),
                       diameter("bolt-diameter", (132.5, 80 + 45 * math.sin(math.pi / 3)), 5, (238, 146), "6X "),
                       diameter("mount-diameter", (175, 120), 4.5, (238, 120), "4X "),
                       {"type": "radial_dimension", "id": "corner-radius", "layer": "DIM",
                        "center": p(8, 102), "chord": p(8 - 8 / math.sqrt(2), 102 + 8 / math.sqrt(2)),
                        "leaderLength": 20, "prefix": "20X ", "textPosition": p(-12, 122)}])
    groups = [([{"name": "OUTLINE", "color": 7, "lineType": "Continuous"}], outline),
              ([{"name": "HOLE", "color": 7, "lineType": "Continuous"}, {"name": "CENTER", "color": 8, "lineType": "CENTER"}], holes),
              ([{"name": "DIM", "color": 3, "lineType": "Continuous"}], dimensions)]
    return [{"schemaVersion": "2d-1.0", "requestId": str(uuid.UUID(int=i + 101)),
             "documentFingerprint": "", "unit": "millimeter", "layers": layers, "entities": entities}
            for i, (layers, entities) in enumerate(groups)]


if __name__ == "__main__":
    root = Path(__file__).resolve().parent
    for filename, plan in zip(["01-outline.json", "02-holes.json", "03-dimensions.json"], stages()):
        (root / filename).write_text(json.dumps(plan, ensure_ascii=False, indent=2), encoding="utf-8")
        print(filename, len(plan["entities"]), "entities")
