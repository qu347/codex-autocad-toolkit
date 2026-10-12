"""Drive the three flange stages through AutoCAD's native /b script entry point.

No desktop input automation, plug-in installation or existing drawing edits.
The supplied LSP must be reviewed and expose CADOUTLINE, CADHOLES, CADDIMS.
"""

import argparse
import hashlib
import json
import math
from pathlib import Path
import subprocess
import tempfile
import time

import ezdxf
from PIL import Image


def lisp_path(path):
    return str(Path(path).resolve()).replace("\\", "/").replace('"', '\\"')


def verify_dxf(path):
    drawing = ezdxf.readfile(path)
    model = drawing.modelspace()
    layers = {name: list(model.query(f'*[layer=="{name}"]'))
              for name in ("OUTLINE", "HOLE", "CENTER", "DIM", "TEXT")}
    outline = layers["OUTLINE"]
    holes = [e for e in layers["HOLE"] if e.dxftype() == "CIRCLE"]
    slots = [e for e in layers["HOLE"] if e.dxftype() == "LWPOLYLINE"]
    pitch = [e for e in layers["CENTER"] if e.dxftype() == "CIRCLE"]
    dims = [e for e in layers["DIM"] if e.dxftype() == "DIMENSION"]
    assert len(model) == 70, f"Expected 70 model entities, got {len(model)}"
    assert len(outline) == 1 and outline[0].closed
    assert len(holes) == 11 and len(slots) == 2 and len(pitch) == 1
    assert len(dims) == 25 and len(layers["TEXT"]) == 4
    assert all(e.closed for e in slots)
    assert drawing.header["$INSUNITS"] == 4
    assert drawing.header["$UCSORG"] == (0, 0, 0)
    assert drawing.header["$UCSXDIR"] == (1, 0, 0)
    assert drawing.header["$UCSYDIR"] == (0, 1, 0)
    assert all(e.dxf.defpoint.z == 0 for e in dims)
    assert all(not e.dxf.get("text") or "<>" in e.dxf.text for e in dims)
    assert all(e.dxf.center.z == 0 for e in holes + pitch)
    assert all(e.dxf.elevation == 0 for e in outline + slots)
    assert len(outline[0]) == 40
    assert all(e.dxf.start.z == e.dxf.end.z == 0 for e in model.query("LINE"))
    assert all(e.dxf.insert.z == 0 for e in model.query("TEXT"))
    vertices = list(outline[0].get_points("xyb"))
    radii = []
    for i, (x, y, bulge) in enumerate(vertices):
        if bulge:
            nx, ny, _ = vertices[(i + 1) % len(vertices)]
            radii.append(math.hypot(nx-x, ny-y) * (1+bulge*bulge)/(4*abs(bulge)))
    assert len(radii) == 20 and max(radii)-min(radii) < 1e-8
    # Rounded values here normalize diagnostics between two native DXF exports;
    # they never change entity coordinates or the trig calculations in the LSP.
    circle_data = [{"layer": e.dxf.layer, "center": [round(v, 12) for v in e.dxf.center],
                    "diameter": round(2*e.dxf.radius, 12)} for e in holes + pitch]
    return {"model_entities": len(model), "layers": {k: len(v) for k, v in layers.items()},
            "closed_outline": True, "actual_circular_holes": len(holes),
            "closed_slots": len(slots), "auxiliary_pitch_circles": len(pitch),
            "native_dimensions": len(dims), "units": "mm", "world_ucs": True,
            "all_model_geometry_z": 0,
            "outline_bounds": [min(v[0] for v in vertices), min(v[1] for v in vertices),
                               max(v[0] for v in vertices), max(v[1] for v in vertices)],
            "fillet_count": len(radii), "fillet_radius": round(radii[0], 12),
            "circles": circle_data,
            "slots": [[[round(v, 12) for v in point] for point in e.get_points("xyb")] for e in slots],
            "dimension_measurements": [round(e.dxf.actual_measurement, 9) for e in dims]}


def script_source(lsp, stem, marker):
    p = lambda suffix: lisp_path(str(stem) + suffix)
    # Read reviewed source as text, retaining newlines for AutoLISP comments.
    # This needs no change to SECURELOAD or TRUSTEDPATHS and no plug-in loader.
    lines = [
        '(vl-load-com)',
        '(setq fd:doc (vla-get-ActiveDocument (vlax-get-acad-object)))',
        '(setq fd:valid (not (ssget "_X" \'((410 . "Model")))))',
        f'(setq fd:input (open "{lisp_path(lsp)}" "r"))',
        '(setq fd:source "(progn\\n")',
        '(while (setq fd:readline (read-line fd:input)) (setq fd:source (strcat fd:source fd:readline "\\n")))',
        '(close fd:input)',
        '(eval (read (strcat fd:source ")")))',
        '(defun fd:count (layer / s) (setq s (ssget "_X" (list (cons 8 layer) \'(410 . "Model")))) (if s (sslength s) 0))',
        f'(setq fd:proof (open "{lisp_path(marker)}" "w"))',
        '(write-line (strcat "ACADVER=" (getvar "ACADVER")) fd:proof)',
        '(close fd:proof)',
        '(defun fd:check (o h c d tcount) (equal (list (fd:count "OUTLINE") (fd:count "HOLE") (fd:count "CENTER") (fd:count "DIM") (fd:count "TEXT")) (list o h c d tcount)))',
        '(defun fd:capture (base / selection) (vla-Regen fd:doc 1) (vla-ZoomExtents (vlax-get-acad-object)) (setq selection (vla-Add (vla-get-SelectionSets fd:doc) "FD_PREVIEW")) (vla-Select selection 5) (vla-Export fd:doc base "BMP" selection) (vla-Delete selection))',
        f'(defun fd:receipt (text / f) (setq f (open "{lisp_path(marker)}" "a")) (write-line text f) (close f))',
    ]
    for stage, command, counts in [
        (1, "CADOUTLINE", "1 0 0 0 0"),
        (2, "CADHOLES", "1 13 27 0 0"),
        (3, "CADDIMS", "1 13 27 25 4"),
    ]:
        lines += [f'(if fd:valid (progn (setq fd:valid nil) (c:{command}) (if (fd:check {counts}) (progn (fd:capture "{p(f"_stage{stage}")}") (fd:receipt "STAGE{stage}=OK") (setq fd:valid T)) (fd:receipt "ERROR=STAGE{stage}"))))',
                  '_.DELAY', '3000']
    # Repetition must leave all generated stages unchanged.
    lines += ['(if fd:valid (progn (setq fd:valid nil) (c:CADOUTLINE) (c:CADHOLES) (c:CADDIMS) (if (fd:check 1 13 27 25 4) (progn (fd:receipt "REPEAT=SKIPPED") (setq fd:valid T)) (fd:receipt "ERROR=REPEAT"))))',
              f'(if fd:valid (progn (setq fd:valid nil) (command-s "_.AUDIT" "_No") (vla-SaveAs fd:doc "{p(".dxf")}" 65) (vla-SaveAs fd:doc "{p(".dwg")}" 64) (fd:receipt "SAVED=OK") (setq fd:valid T)))',
              '(if (not fd:valid) (fd:receipt "ERROR=STOPPED"))', '(princ)']
    return "\n".join(lines) + "\n"


def reopen_native(acad, dwg, stem):
    """Reopen our saved DWG in the native engine without changing the desktop view."""
    core = acad.with_name("accoreconsole.exe")
    if not core.is_file():
        raise FileNotFoundError(core)
    with tempfile.TemporaryDirectory(prefix="fd-reopen-") as folder:
        folder = Path(folder)
        receipt = folder / "reopen.txt"
        export = folder / "reopened.dxf"
        script = folder / "verify.scr"
        source = [
            '(setvar "FILEDIA" 0)',
            '(command-s "_.AUDIT" "_No")',
            f'(command-s "_.DXFOUT" "{lisp_path(export)}" "16")',
            f'(setq fd:f (open "{lisp_path(receipt)}" "w"))',
            '(write-line "REOPENED" fd:f)', '(close fd:f)',
            '_.QUIT', '_Yes',
        ]
        script.write_text("\n".join(source) + "\n", encoding="cp936")
        result = subprocess.run([str(core), "/i", str(dwg), "/s", str(script)],
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=60)
        raw = result.stdout
        output = raw.decode("utf-16-le" if raw.count(b"\x00") > len(raw)/5 else "cp936",
                            errors="replace")
        log = Path(str(stem) + "_reopen.log")
        log.write_text(output, encoding="utf-8")
        if result.returncode or not receipt.is_file() or not export.is_file():
            raise RuntimeError(f"Native DWG reopen failed; inspect {log}")
        return verify_dxf(export)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--acad", type=Path, required=True)
    parser.add_argument("--template", type=Path, required=True)
    parser.add_argument("--lsp", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    for path in (args.acad, args.template, args.lsp):
        if not path.is_file():
            raise FileNotFoundError(path)
    output = args.output.resolve()
    if output.suffix.lower() != ".dwg":
        raise ValueError("Output must be a new .dwg path")
    stem = output.with_suffix("")
    suffixes = [".dwg", ".dxf", "_run.scr", "_receipt.txt", "_verification.json", "_reopen.log"]
    suffixes += [f"_stage{i}.{ext}" for i in range(1, 4) for ext in ("bmp", "png")]
    for suffix in suffixes:
        if Path(str(stem) + suffix).exists():
            raise FileExistsError(f"Choose a new output name; refusing overwrite: {stem}{suffix}")
    output.parent.mkdir(parents=True, exist_ok=True)
    marker = Path(str(stem) + "_receipt.txt")
    script = Path(str(stem) + "_run.scr")
    script.write_text(script_source(args.lsp.resolve(), stem, marker), encoding="cp936")
    # Native desktop startup switch; no mouse, keyboard, screenshots or UI selectors.
    process = subprocess.Popen([str(args.acad), "/nologo", "/t", str(args.template.resolve()),
                                "/b", str(script)], cwd=str(output.parent))
    print(f"AutoCAD PID={process.pid}; new output={output}", flush=True)
    deadline = time.monotonic() + 150
    seen = set()
    while time.monotonic() < deadline:
        receipt = marker.read_text(encoding="cp936", errors="replace") if marker.exists() else ""
        if "ERROR=" in receipt:
            raise RuntimeError(f"Native stage failed; later stages skipped. Inspect {marker}")
        for stage in range(1, 4):
            if f"STAGE{stage}=OK" in receipt and stage not in seen:
                bmp = Path(str(stem) + f"_stage{stage}.bmp")
                png = bmp.with_suffix(".png")
                with Image.open(bmp) as preview:
                    preview.save(png)
                seen.add(stage)
                print(f"Stage {stage} actually completed; preview={png}", flush=True)
        if "SAVED=OK" in receipt:
            break
        if process.poll() is not None:
            raise RuntimeError(f"AutoCAD exited before completion; inspect {marker} and {script}")
        time.sleep(.3)
    else:
        raise TimeoutError(f"Native run did not finish; no automatic rerun. Inspect {marker} and {script}")
    assert seen == {1, 2, 3}, "Not all three native stages and previews completed"
    assert "REPEAT=SKIPPED" in receipt, "Repetition check did not complete"
    actual = verify_dxf(output.with_suffix(".dxf"))
    reopened = reopen_native(args.acad.resolve(), output, stem)
    assert reopened == actual, "Saved/reopened drawing differs from desktop export"
    report = {"desktop_receipt": marker.read_text(encoding="cp936"),
              "desktop_pid": process.pid, "lsp": str(args.lsp.resolve()),
              "lsp_sha256": hashlib.sha256(args.lsp.read_bytes()).hexdigest(),
              "dwg": str(output), "dxf": str(output.with_suffix(".dxf")),
              "actual_drawing": actual, "native_dwg_reopen": reopened,
              "computer_use": False, "mcp_installation": False}
    evidence = Path(str(stem) + "_verification.json")
    evidence.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Verified actual native drawing, repetition and DWG reopen; evidence={evidence}", flush=True)


if __name__ == "__main__":
    main()
