"""Read Unity runtime telemetry; no image generation or editing."""
import json
from pathlib import Path

root = Path(__file__).parent
summary = {}
for version in (1, 2):
    data = json.loads((root / f"captures-v{version}" / "runtime-evidence.json").read_text())
    frames = data["frames"]
    skins = {}
    for name in (s["name"] for s in frames[0]["skins"]):
        samples = [s for f in frames for s in f["skins"] if s["name"] == name]
        skins[name] = {
            "allReady": all(s["ready"] for s in samples),
            "vertices": samples[0]["vertices"],
            "maxFlippedVisibleTriangles": max(s["invertedVisibleTriangles"] for s in samples),
            "minSignedAreaRatio": min(s["minAreaRatio"] for s in samples),
            "maxSignedAreaRatio": max(s["maxAreaRatio"] for s in samples),
            "maxVertexDisplacement": max(s["maxVertexDisplacement"] for s in samples),
        }
    transitions = []
    previous = None
    for f in frames:
        if f["state"] != previous:
            transitions.append({"frame": f["frame"], "time": f["time"], "state": f["state"]})
        previous = f["state"]
    summary[f"v{version}"] = {
        "unity": data["unityVersion"], "frames": len(frames), "fps": 24,
        "screen": frames[0]["screen"],
        "initialProjectedSize": frames[0]["projectedSize"],
        "maxFootDrift": max(f["footDrift"] for f in frames),
        "nearWristXRange": [min(f["nearWrist"]["x"] for f in frames), max(f["nearWrist"]["x"] for f in frames)],
        "nearWristYRange": [min(f["nearWrist"]["y"] for f in frames), max(f["nearWrist"]["y"] for f in frames)],
        "stateTransitions": transitions, "skins": skins,
    }
(root / "metrics-summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
print(json.dumps(summary, indent=2))
