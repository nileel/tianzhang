"""Copy R3 evidence, summarize real frame timing, and encode optional timestamped captures.

Usage: python summarize.py <src/Logs/r3/label> <destination> [--video]
Video mode requires imageio-ffmpeg. It preserves capture intervals with a 1 ms input timebase.
"""
import csv
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys


def read_csv(path):
    with path.open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


source, destination = map(Path, sys.argv[1:3])
destination.mkdir(parents=True, exist_ok=True)
summaries = []
for folder in sorted(p for p in source.iterdir() if p.is_dir()):
    output = destination / folder.name
    output.mkdir(exist_ok=True)
    for name in ("command.txt", "route.txt", "stages.csv", "frames.csv", "summary.txt", "captures.csv"):
        if (folder / name).exists():
            shutil.copy2(folder / name, output / name)
    frames = read_csv(folder / "frames.csv")
    rendered = [f for f in frames if f["phase"] == "rendered"]
    flying = [f for f in rendered if f["impacted"] == "False"]
    impact = next(f for f in rendered if f["impacted"] == "True")
    created = frames[0]
    destroyed = frames[-1]
    stages = read_csv(folder / "stages.csv")
    command = (folder / "command.txt").read_text(encoding="utf-8-sig")
    command_at = float(re.search(r"commandAt=([\d.]+)", command)[1])
    provider_after = float(re.search(r"providerAfter=([\d.]+)", command)[1])
    returned_at = float(re.search(r"returnedAt=([\d.]+)", command)[1])
    first = rendered[0]
    assert int(first["frame"]) == int(created["frame"]) + 1, "Missed first Update frame"
    assert abs(float(first["elapsed"]) - float(first["delta_s"])) <= 1e-6
    assert len(flying) >= 3
    assert all(f["head_active"] == "True" and f["renderer_enabled"] == "True" and
               f["tail_active_count"] == "4" and float(f["screen_z"]) > 0 for f in flying)
    hud = [float(s["ms"]) for s in stages if s["stage"] == "hud"]
    summary = {
        "case": folder.name,
        "flying_frames": len(flying),
        "first_update_delta_ms": float(first["delta_s"]) * 1000,
        "max_flight_delta_ms": max(float(f["delta_s"]) for f in flying) * 1000,
        "impact_elapsed_s": float(impact["elapsed"]),
        "impact_observed_after_provider_ms": (float(impact["realtime"]) - provider_after) * 1000,
        "last_existing_elapsed_s": float(rendered[-1]["elapsed"]),
        "first_missing_after_provider_ms": (float(destroyed["realtime"]) - provider_after) * 1000,
        "confirmation_ms": (returned_at - command_at) * 1000,
        "max_hud_ms": max(hud) if hud else None,
        "source_target": command.splitlines()[3],
    }
    if "--video" in sys.argv:
        import imageio_ffmpeg
        captures = read_csv(folder / "captures.csv")
        concat = folder / "video.ffconcat"
        lines = ["ffconcat version 1.0"]
        for i, capture in enumerate(captures):
            lines += ["file '" + (folder / capture["file"]).resolve().as_posix() + "'", "option framerate 1000"]
            if i + 1 < len(captures):
                lines.append("duration " + str(float(captures[i + 1]["realtime"]) - float(capture["realtime"])))
        concat.write_text("\n".join(lines) + "\n", encoding="utf-8")
        subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), "-hide_banner", "-loglevel", "error", "-y", "-safe", "0",
                        "-i", str(concat), "-fps_mode", "vfr", "-c:v", "libx264", "-crf", "18",
                        "-pix_fmt", "yuv420p", "-video_track_timescale", "1000000", str(output / "flight.mp4")], check=True)
        by_frame = {int(f["frame"]): f for f in rendered}
        flight_captures = [c for c in captures if int(c["frame"]) in by_frame and by_frame[int(c["frame"])]["impacted"] == "False"]
        assert len(flight_captures) >= 3, "Recording must actually contain flying frames"
        for name, part in zip(("flight-early.jpg", "flight-middle.jpg", "flight-late.jpg"),
                              (flight_captures[0], flight_captures[len(flight_captures) // 2], flight_captures[-1])):
            shutil.copy2(folder / part["file"], output / name)
        summary["recorded_flight_frames"] = len(flight_captures)
        summary["capture_duration_s"] = float(captures[-1]["realtime"]) - float(captures[0]["realtime"])
    summaries.append(summary)
(destination / "summary.json").write_text(json.dumps(summaries, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(summaries, ensure_ascii=False, indent=2))
