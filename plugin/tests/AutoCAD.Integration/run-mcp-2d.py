"""Real MCP STDIO -> named pipe -> AutoCAD 2026 integration check.

Requires a built Bridge and plugin, AUTOCAD_PATH, and a local acadiso.dwt.
All drawing work is performed in a new copied fixture, never a user DWG.
"""
import argparse
import collections
import io
import json
import os
from pathlib import Path
import queue
import shutil
import subprocess
import threading
import time
import uuid


class McpClient:
    def __init__(self, exe):
        self.process = subprocess.Popen([str(exe)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        stderr=subprocess.PIPE, creationflags=subprocess.CREATE_NO_WINDOW)
        self.messages = queue.Queue()
        self.errors = collections.deque(maxlen=30)
        self.sequence = 0
        threading.Thread(target=self._read, daemon=True).start()
        threading.Thread(target=self._read_errors, daemon=True).start()
        self.rpc("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                               "clientInfo": {"name": "aicad-integration", "version": "1.0"}})
        self._write({"jsonrpc": "2.0", "method": "notifications/initialized"})

    def _read(self):
        for line in self.process.stdout:
            self.messages.put(json.loads(line))

    def _read_errors(self):
        for line in self.process.stderr:
            self.errors.append(line.decode("utf-8", errors="replace").rstrip())

    def _write(self, message):
        self.process.stdin.write((json.dumps(message, ensure_ascii=False) + "\n").encode("utf-8"))
        self.process.stdin.flush()

    def rpc(self, method, params):
        self.sequence += 1
        request_id = self.sequence
        self._write({"jsonrpc": "2.0", "id": request_id, "method": method, "params": params})
        deadline = time.monotonic() + 45
        while time.monotonic() < deadline:
            message = self.messages.get(timeout=max(0.1, deadline - time.monotonic()))
            if message.get("id") != request_id:
                continue
            if "error" in message:
                raise RuntimeError(message["error"])
            return message["result"]
        raise TimeoutError(method)

    def close(self):
        self.process.stdin.close()
        try:
            self.process.wait(5)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait()


class CoreSession:
    def __init__(self, acad_path, plugin, template, output):
        self.input = output / "mcp2d-input.dwg"
        shutil.copyfile(template, self.input)
        self.logs = collections.deque(maxlen=50)
        self.process = subprocess.Popen([str(acad_path / "accoreconsole.exe"), "/i", str(self.input), "/l", "en-US"],
                                        cwd=output, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)
        threading.Thread(target=self._read, daemon=True).start()
        self.send('(setq aicadOldTrustedPaths (getvar "TRUSTEDPATHS"))')
        self.send('(setvar "TRUSTEDPATHS" (strcat aicadOldTrustedPaths ";' + str(plugin.parent).replace("\\", "/") + '"))')
        self.send("_.NETLOAD")
        self.send(str(plugin))
        self.send('(setvar "TRUSTEDPATHS" aicadOldTrustedPaths)')
        descriptor = Path(os.environ["LOCALAPPDATA"]) / "AiCad/sessions" / f"{self.process.pid}.json"
        deadline = time.monotonic() + 20
        while not descriptor.exists() and time.monotonic() < deadline:
            if self.process.poll() is not None:
                break
            time.sleep(0.1)
        if not descriptor.exists():
            self.close()
            raise RuntimeError("Native plugin did not start:\n" + "\n".join(self.logs))

    def _read(self):
        for line in io.TextIOWrapper(self.process.stdout, encoding="utf-16-le", errors="replace"):
            self.logs.append(line.rstrip())

    def send(self, line):
        self.process.stdin.write((line + "\n").encode("utf-8"))
        self.process.stdin.flush()

    def tool(self, client, name, arguments=None, native_calls=1, allow_error=False):
        # Core Console lacks desktop Idle events. Supply one command per native
        # RPC, including Bridge preflight calls, without leaving a pump backlog.
        for _ in range(native_calls):
            self.send("AICADWAITFORRPC")
        result = client.rpc("tools/call", {"name": name, "arguments": arguments or {}})
        if result.get("isError"):
            if allow_error:
                return result
            raise RuntimeError(f"{name}: {result.get('content')}")
        if result.get("structuredContent") is not None:
            return result["structuredContent"]
        for block in result.get("content", []):
            if block.get("type") == "text":
                return json.loads(block["text"])
        raise RuntimeError(f"{name} returned no structured evidence")

    def close(self):
        if self.process.poll() is None:
            self.send("_.QUIT")
            self.send("_Y")
            try:
                self.process.wait(10)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--template", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--reopen", type=Path)
    parser.add_argument("--stages", type=Path)
    args = parser.parse_args()
    args.output = args.output.resolve()
    repo = Path(__file__).resolve().parents[2]
    acad = Path(os.environ["AUTOCAD_PATH"])
    plugin = repo / "src/AiCad.AutoCAD/bin/Release/net8.0-windows/AiCad.AutoCAD.dll"
    bridge = repo / "src/AiCad.Bridge/bin/Release/net8.0/win-x64/publish/AiCad.Bridge.exe"
    args.output.mkdir(parents=True, exist_ok=False)
    core = CoreSession(acad, plugin, args.reopen or args.template, args.output)
    client = None
    try:
        client = McpClient(bridge)
        names = {tool["name"] for tool in client.rpc("tools/list", {})["tools"]}
        assert {"cad_draw_2d", "cad_get_layers", "cad_get_geometry", "cad_save_drawing"} <= names
        health = core.tool(client, "cad_health")
        assert health["autoCadProcessId"] == core.process.pid
        context = core.tool(client, "cad_get_context")
        assert context["unit"].lower() == "millimeter", context
        if args.reopen:
            entities = core.tool(client, "cad_get_entities", {"layout": "Model", "cursor": None})
            dimensions = core.tool(client, "cad_get_dimensions", {"layout": "Model", "cursor": None})
            assert entities["totalCount"] == 66 and dimensions["totalCount"] == 25
            source_evidence = json.loads((args.reopen.parent / "mcp-evidence.json").read_text(encoding="utf-8"))
            last_plan = source_evidence["stageResults"][-1]["plan"]
            status = core.tool(client, "cad_get_request_status", {"requestId": last_plan["requestId"]})
            assert status["status"] == "completed"
            duplicate = core.tool(client, "cad_draw_2d", {"planJson": json.dumps(last_plan)}, native_calls=3)
            assert duplicate["handles"] == source_evidence["stageResults"][-1]["result"]["handles"]
            evidence = {"entities": entities, "dimensions": dimensions, "requestStatus": status, "duplicate": duplicate}
            (args.output / "reopen-evidence.json").write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding="utf-8")
            print("AICAD_TEST_PASS mcp_2d_dwg_reopen")
            return
        plan = {"schemaVersion": "2d-1.0", "requestId": str(uuid.uuid4()),
                "documentFingerprint": context["documentFingerprint"], "unit": "millimeter",
                "layers": [{"name": "MCP-HOLE", "color": 7, "lineType": "Continuous"}],
                "entities": [{"type": "circle", "id": "test-bore", "layer": "MCP-HOLE",
                              "center": {"x": 110, "y": 80}, "radius": 30}]}
        if not args.stages:
            plan["entities"].extend([
                {"type": "line", "id": "test-line", "layer": "MCP-HOLE", "start": {"x": 0, "y": 0}, "end": {"x": 20, "y": 0}},
                {"type": "arc", "id": "test-arc", "layer": "MCP-HOLE", "center": {"x": 40, "y": 10}, "radius": 5,
                 "startAngleDegrees": 0, "endAngleDegrees": 90},
                {"type": "polyline", "id": "test-slot", "layer": "MCP-HOLE", "closed": True,
                 "vertices": [{"x": 60, "y": -6, "bulge": 0}, {"x": 78, "y": -6, "bulge": 1},
                              {"x": 78, "y": 6, "bulge": 0}, {"x": 60, "y": 6, "bulge": 1}]},
                {"type": "aligned_dimension", "id": "test-aligned", "layer": "MCP-HOLE",
                 "first": {"x": 0, "y": 0}, "second": {"x": 20, "y": 0}, "linePoint": {"x": 10, "y": -10}}])
        payload = {"planJson": json.dumps(plan)}
        stage_results = []
        all_handles = {}
        if args.stages:
            for file in sorted(args.stages.glob("0?-*.json")):
                plan = json.loads(file.read_text(encoding="utf-8"))
                context = core.tool(client, "cad_get_context")
                plan["documentFingerprint"] = context["documentFingerprint"]
                plan["requestId"] = str(uuid.uuid4())
                payload = {"planJson": json.dumps(plan)}
                created = core.tool(client, "cad_draw_2d", payload, native_calls=2)
                assert len(created["handles"]) == len(plan["entities"])
                stage_results.append({"stage": file.name, "plan": plan, "result": created})
                all_handles.update(created["handles"])
            assert len(stage_results) == 3 and len(all_handles) == 66
        else:
            created = core.tool(client, "cad_draw_2d", payload, native_calls=2)
            all_handles.update(created["handles"])
        duplicate = core.tool(client, "cad_draw_2d", payload, native_calls=3)
        assert duplicate["batchId"] == created["batchId"] and duplicate["handles"] == created["handles"]
        geometry = {"items": []}
        handles = list(all_handles.values())
        for offset in range(0, len(handles), 20):
            page = core.tool(client, "cad_get_geometry", {"handlesJson": json.dumps(handles[offset:offset + 20])})
            geometry["items"].extend(page["items"])
            geometry["documentFingerprint"] = page["documentFingerprint"]
        if args.stages:
            outline = next(item["data"] for item in geometry["items"] if item["handle"] == all_handles["flange-outline"])
            assert outline["closed"] and len(outline["vertices"]) == 40
            radii = []
            for i, v in enumerate(outline["vertices"]):
                if v["bulge"]:
                    following = outline["vertices"][(i + 1) % 40]
                    chord = ((v["x"] - following["x"]) ** 2 + (v["y"] - following["y"]) ** 2) ** 0.5
                    radii.append(chord * (1 + v["bulge"] ** 2) / (4 * abs(v["bulge"])))
            assert len(radii) == 20 and all(abs(radius - 8) < 1e-6 for radius in radii)
            actual_holes = [item for item in geometry["items"] if item["entityType"] == "Circle" and item["layer"] == "HOLE"]
            assert len(actual_holes) == 11
            actual_dimensions = [item for item in geometry["items"] if "Dimension" in item["entityType"]]
            assert len(actual_dimensions) == 25
            for name, expected in {"width": 220, "height": 160, "slot-length": 30, "slot-width": 12,
                                   "bore-diameter": 60, "pitch-diameter": 90, "bolt-diameter": 10,
                                   "mount-diameter": 9, "corner-radius": 8}.items():
                actual = next(item["data"]["measurement"] for item in actual_dimensions if item["handle"] == all_handles[name])
                assert abs(actual - expected) < 1e-6, (name, actual, expected)
        else:
            assert geometry["items"][0]["data"]["radius"] == 30
            by_handle = {item["handle"]: item for item in geometry["items"]}
            assert by_handle[all_handles["test-arc"]]["data"]["endAngleDegrees"] == 90
            assert by_handle[all_handles["test-slot"]]["data"]["closed"]
            assert by_handle[all_handles["test-aligned"]]["data"]["measurement"] == 20
        layers = core.tool(client, "cad_get_layers")
        assert any(layer["name"] == ("HOLE" if args.stages else "MCP-HOLE") for layer in layers["items"])
        conflicting = json.loads(payload["planJson"])
        conflicting["entities"][0]["id"] += "-changed"
        error = core.tool(client, "cad_draw_2d", {"planJson": json.dumps(conflicting)}, native_calls=3, allow_error=True)
        assert error["isError"] and "REQUEST_ID_CONFLICT" in str(error["content"])
        context = core.tool(client, "cad_get_context")
        saved = core.tool(client, "cad_save_drawing", {
            "documentFingerprint": context["documentFingerprint"],
            "dwgPath": str((args.output / "mcp2d.dwg").resolve()),
            "dxfPath": str((args.output / "mcp2d.dxf").resolve())}, native_calls=2)
        assert saved["success"] and all(Path(file["path"]).stat().st_size == file["bytes"] > 0 for file in saved["files"])
        evidence = {"tools": sorted(names), "health": health, "created": created,
                    "duplicate": duplicate, "geometry": geometry, "layers": layers, "saved": saved,
                    "stageResults": stage_results, "conflictingRequest": error}
        rejected = core.tool(client, "cad_save_drawing", {"documentFingerprint": context["documentFingerprint"],
                              "dwgPath": saved["files"][0]["path"]}, native_calls=0, allow_error=True)
        assert rejected["isError"] and "OUTPUT_EXISTS" in str(rejected["content"])
        evidence["overwriteRejected"] = rejected
        # Force a layer conflict after a new layer was added: the entire stage
        # must roll back, including that temporary layer.
        context = core.tool(client, "cad_get_context")
        conflict_layer = "HOLE" if args.stages else "MCP-HOLE"
        rollback_plan = {"schemaVersion": "2d-1.0", "requestId": str(uuid.uuid4()),
                         "documentFingerprint": context["documentFingerprint"], "unit": "millimeter",
                         "layers": [{"name": "ROLLBACK-TEMP", "color": 7, "lineType": "Continuous"},
                                    {"name": conflict_layer, "color": 1, "lineType": "Continuous"}],
                         "entities": []}
        rejected = core.tool(client, "cad_draw_2d", {"planJson": json.dumps(rollback_plan)}, native_calls=2, allow_error=True)
        assert rejected["isError"] and "LAYER_CONFLICT" in str(rejected["content"])
        after = core.tool(client, "cad_get_layers")
        assert all(layer["name"] != "ROLLBACK-TEMP" for layer in after["items"])
        evidence["layerRollback"] = rejected
        if not args.stages:
            # Width edits affect real polyline geometry even if vertices and
            # bulges do not change. Preserve such a user edit during undo.
            slot_handle = all_handles["test-slot"]
            core.send(f'(setq slotEntity (handent "{slot_handle}"))')
            core.send('(entmod (subst (cons 43 2.0) (assoc 43 (entget slotEntity)) (entget slotEntity)))')
            rejected = core.tool(client, "cad_undo_batch", {"batchId": created["batchId"]}, allow_error=True)
            assert rejected.get("isError") and "TARGET_CHANGED" in str(rejected.get("content")), rejected
            evidence["editedPolylineWidthUndoRejected"] = rejected
            retry = core.tool(client, "cad_draw_2d", payload, native_calls=3, allow_error=True)
            assert retry.get("isError") and "TARGET_CHANGED" in str(retry.get("content")), retry
            evidence["editedPolylineWidthRetryRejected"] = retry
            core.send('(entmod (subst (cons 43 0.0) (assoc 43 (entget slotEntity)) (entget slotEntity)))')
            # Simulate an edit to our own disposable fixture, then test the
            # checked undo boundary through the actual MCP.
            handle = all_handles["test-bore"]
            core.send(f'(setq e (handent "{handle}"))')
            core.send('(entmod (subst (cons 40 31.0) (assoc 40 (entget e)) (entget e)))')
            rejected = core.tool(client, "cad_undo_batch", {"batchId": created["batchId"]}, allow_error=True)
            assert rejected["isError"] and "TARGET_CHANGED" in str(rejected["content"]), rejected
            evidence["editedUndoRejected"] = rejected
            core.send('(entmod (subst (cons 40 30.0) (assoc 40 (entget e)) (entget e)))')
            undone = core.tool(client, "cad_undo_batch", {"batchId": created["batchId"]})
            assert undone["success"]
            status = core.tool(client, "cad_get_request_status", {"requestId": plan["requestId"]})
            assert status["status"] == "undone"
            evidence["undo"] = undone
        (args.output / "mcp-evidence.json").write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding="utf-8")
        print("AICAD_TEST_PASS mcp_2d_stdio_native")
    finally:
        if client:
            client.close()
        core.close()
        (args.output / "core.log").write_text("\n".join(core.logs), encoding="utf-8")


if __name__ == "__main__":
    main()
