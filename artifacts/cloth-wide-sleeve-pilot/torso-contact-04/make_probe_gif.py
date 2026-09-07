"""Encode the unchanged-input Unity diagnostic capture, not a repaired sleeve demonstration."""
from pathlib import Path
import hashlib
from PIL import Image

source = Path('D:/Temp/TianZhang-Blender/cloth-wide-sleeve-torso-04-probe-parts-capture')
destination = Path(__file__).resolve().parent / 'unity-body-probe-baseline.gif'
paths = sorted(source.glob('frame_*.png'))
assert len(paths) == 96 and paths[0].name == 'frame_0000.png' and paths[-1].name == 'frame_0095.png'
frames = []
for path in paths:
    with Image.open(path) as frame:
        assert frame.size == (1280, 720)
        frames.append(frame.convert('RGB').resize((960, 540), Image.Resampling.LANCZOS).quantize(colors=128))
frames[0].save(destination, save_all=True, append_images=frames[1:], loop=0,
               duration=[80, 80, 90] * 32, optimize=False, disposal=2)
duration = 0
with Image.open(destination) as decoded:
    assert decoded.n_frames == 96
    for i in range(decoded.n_frames):
        decoded.seek(i)
        decoded.load()
        duration += decoded.info['duration']
assert duration == 8000
print(destination.name, destination.stat().st_size, 'bytes, 96 frames / 8000 ms, SHA256',
      hashlib.sha256(destination.read_bytes()).hexdigest())
