"""Encode real Unity PNGs, retaining the entire startup and all original actions/views."""
from pathlib import Path
import hashlib
import json
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
SOURCE = Path('D:/Temp/TianZhang-Blender/cloth-wide-sleeve-initialization-07-visible-capture')


def frame(index):
    with Image.open(SOURCE / f'frame_{index:04d}.png') as image:
        assert image.size == (1280, 720)
        return image.convert('RGB')


def encode(name, indices, size):
    frames = [frame(i).resize(size, Image.Resampling.LANCZOS).quantize(colors=128) for i in indices]
    path = ROOT / name
    durations = [80, 80, 90] * (len(frames) // 3)
    frames[0].save(path, save_all=True, append_images=frames[1:], loop=0, duration=durations, optimize=False, disposal=2)
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
    assert len(list(SOURCE.glob('frame_*.png'))) == 1152
    offsets = [0, 10, 24, 47, 84, 107, 118, 130, 188]
    sheet = Image.new('RGB', (len(offsets)*320, 6*360))
    for direction in range(6):
        for column, offset in enumerate(offsets):
            index = direction*192+offset
            tile = frame(index).crop((0, 0, 640, 720)).resize((320, 360), Image.Resampling.LANCZOS)
            ImageDraw.Draw(tile).text((8, 65), f'frame {index} / cycle {offset/12:.2f}s', fill='white')
            sheet.paste(tile, (column*320, direction*360))
    sheet.save(ROOT / 'six-direction-contact-sheet.png')
    media = [encode('unity-initialization-07-yaw150.gif', range(192, 384), (1280, 720)),
             encode('unity-initialization-07-full.gif', range(1152), (960, 540))]
    aggregate = hashlib.sha256()
    for index in range(1152):
        path = SOURCE / f'frame_{index:04d}.png'
        aggregate.update(path.name.encode()+hashlib.sha256(path.read_bytes()).digest())
    manifest = {'source': str(SOURCE), 'sourcePngCount': 1152,
                'sourceOrderedNamesAndHashDigest': aggregate.hexdigest(), 'media': media,
                'boundary': 'Real Unity screenshots only, not AI video. Entire initialization and all three '
                            'views retained; full GIF contains all six independent directions.'}
    (ROOT / 'media-manifest.json').write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(media, indent=2))


if __name__ == '__main__':
    main()
