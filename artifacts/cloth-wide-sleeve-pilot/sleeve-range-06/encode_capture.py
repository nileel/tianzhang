"""Encode the single real Unity run; retain all actions/views, including failed poses."""
from pathlib import Path
import hashlib
import json
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
SOURCE = Path('D:/Temp/TianZhang-Blender/cloth-wide-sleeve-range-06-capture')
OLD = Path('D:/Temp/TianZhang-Blender/cloth-wide-sleeve-transition-05-capture')


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
            'sourceCaptureIndices': [indices[0], indices[-1]],
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}


def main():
    assert len(list(SOURCE.glob('frame_*.png'))) == 864
    # Smallest useful overview first, then full temporal evidence.
    sheet = Image.new('RGB', (7*320, 6*360))
    for direction in range(6):
        for column, offset in enumerate([18, 36, 59, 66, 82, 108, 140]):
            index = direction*144+offset
            tile = frame(index).crop((0, 0, 640, 720)).resize((320, 360), Image.Resampling.LANCZOS)
            ImageDraw.Draw(tile).text((8, 65), f'frame {index} / local {offset/12:.2f}s', fill='white')
            sheet.paste(tile, (column*320, direction*360))
    sheet.save(ROOT / 'six-direction-contact-sheet.png')
    evidence = [encode('unity-range-06-comparison.gif', range(144, 288), (1280, 720), True),
                encode('unity-range-06-yaw150.gif', range(144, 288), (1280, 720)),
                encode('unity-range-06-full.gif', range(864), (960, 540))]
    aggregate = hashlib.sha256()
    for index in range(864):
        path = SOURCE / f'frame_{index:04d}.png'
        aggregate.update(path.name.encode()+hashlib.sha256(path.read_bytes()).digest())
    manifest = {'source': str(SOURCE), 'sourcePngCount': 864,
                'sourceOrderedNamesAndHashDigest': aggregate.hexdigest(), 'media': evidence,
                'boundary': 'Real Unity screenshots only; comparison is old-left/new-right same-time near views. Full GIF retains all three views and all six directions.'}
    (ROOT / 'media-manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
    print(json.dumps(evidence, indent=2))


if __name__ == '__main__':
    main()
