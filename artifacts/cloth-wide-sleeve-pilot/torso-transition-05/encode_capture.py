"""Encode real Unity frames without hiding views/actions; only resize, crop comparison, and GIF quantization."""
from pathlib import Path
import hashlib
import json
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
SOURCE = Path('D:/Temp/TianZhang-Blender/cloth-wide-sleeve-transition-05-capture')
OLD = Path('D:/Temp/TianZhang-Blender/cloth-wide-sleeve-pin-release-01-capture')


def frame(index, comparison=False):
    with Image.open(SOURCE / f'frame_{index:04d}.png') as image:
        assert image.size == (1280, 720)
        current = image.convert('RGB')
    if not comparison:
        return current
    with Image.open(OLD / f'frame_{index:04d}.png') as previous:
        pair = Image.new('RGB', (1280, 720))
        pair.paste(previous.crop((0, 0, 640, 720)), (0, 0))
        pair.paste(current.crop((0, 0, 640, 720)), (640, 0))
        return pair


def encode(name, indices, size, comparison=False):
    frames = [frame(i, comparison).resize(size, Image.Resampling.LANCZOS).quantize(colors=128) for i in indices]
    path = ROOT / name
    duration = [80, 80, 90] * (len(frames) // 3)
    frames[0].save(path, save_all=True, append_images=frames[1:], loop=0, duration=duration, optimize=False, disposal=2)
    total = 0
    with Image.open(path) as image:
        assert image.n_frames == len(frames)
        for i in range(image.n_frames):
            image.seek(i)
            image.load()
            total += image.info['duration']
    assert total == len(frames) * 1000 // 12
    return {'file': name, 'frames': len(frames), 'durationMs': total, 'bytes': path.stat().st_size,
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}


assert len(list(SOURCE.glob('frame_*.png'))) == 864
evidence = [encode('unity-transition-05-full.gif', range(864), (960, 540)),
            encode('unity-transition-05-yaw150.gif', range(144, 288), (1280, 720)),
            encode('unity-transition-05-comparison.gif', range(144, 288), (1280, 720), True)]
sheet = Image.new('RGB', (7 * 320, 6 * 360))
for direction in range(6):
    for column, offset in enumerate([18, 36, 59, 66, 82, 108, 140]):
        index = direction * 144 + offset
        tile = frame(index).crop((0, 0, 640, 720)).resize((320, 360), Image.Resampling.LANCZOS)
        ImageDraw.Draw(tile).text((8, 65), f'frame {index} / local {offset / 12:.2f}s', fill='white')
        sheet.paste(tile, (column * 320, direction * 360))
sheet.save(ROOT / 'six-direction-contact-sheet.png')
(ROOT / 'media-manifest.json').write_text(json.dumps(evidence, indent=2) + '\n')
print(json.dumps(evidence, indent=2))
