"""Package R4 runtime evidence and a three-segment passage clip.
The gaps between captured passages (return moves/state fixtures) are omitted.
Each recorded segment retains its original capture intervals.
"""
import csv,json,shutil,subprocess
from pathlib import Path
import imageio_ffmpeg
root=Path(__file__).resolve().parents[4]
source=root/'src/Logs/r4'
destination=Path(__file__).resolve().parent/'runtime'
ffmpeg=imageio_ffmpeg.get_ffmpeg_exe()
summary=[]
for width in (1366,1920):
    folder=source/str(width); out=destination/str(width); out.mkdir(parents=True,exist_ok=True)
    rows=list(csv.DictReader((folder/'captures.csv').open(encoding='utf-8-sig')))
    ledger=list(csv.DictReader((folder/'ap-ledger.csv').open(encoding='utf-8-sig')))
    crossings=[r for r in ledger if r['stage'].startswith('mid_low_cover')]
    assert len(crossings)==12 and all(int(r['preview_ap_units'])==25000==int(r['before_ap_units'])-int(r['after_ap_units']) for r in crossings)
    groups=[[]]
    for r in rows:
        if groups[-1] and float(r['realtime'])-float(groups[-1][-1]['realtime'])>.2: groups.append([])
        groups[-1].append(r)
    assert len(groups)==3, 'Review capture gaps before selecting segment boundaries'
    lines=['ffconcat version 1.0']
    segments=[]
    for state,group in enumerate(groups):
        assert len(group)>10
        segments.append({'state':state,'frames':len(group),'start':float(group[0]['realtime']),'end':float(group[-1]['realtime'])})
        for i,row in enumerate(group):
            duration=float(group[i+1]['realtime'])-float(row['realtime']) if i+1<len(group) else 1/30
            lines.extend(["file '"+(folder/row['file']).resolve().as_posix()+"'",'option framerate 1000',f'duration {duration:.6f}'])
        shutil.copy2(folder/group[len(group)//2]['file'],out/f'passage-state-{state}.jpg')
    lines.extend(["file '"+(folder/rows[-1]['file']).resolve().as_posix()+"'",'option framerate 1000'])
    concat=folder/'r4-passages.ffconcat';concat.write_text('\n'.join(lines)+'\n',encoding='utf-8')
    video=out/'a-wall-three-passages.mp4'
    subprocess.run([ffmpeg,'-hide_banner','-loglevel','error','-y','-safe','0','-i',str(concat),'-fps_mode','vfr','-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-video_track_timescale','1000000',str(video)],check=True)
    subprocess.run([ffmpeg,'-hide_banner','-loglevel','error','-i',str(video),'-fps_mode','passthrough','-f','null','-'],check=True)
    for file in list(folder.glob('*.png'))+[folder/'captures.csv',folder/'ap-ledger.csv']:shutil.copy2(file,out/file.name)
    summary.append({'resolution':f'{width}x{768 if width==1366 else 1080}','crossings':12,'crossingCostAP':25,'segments':segments,'video':'a-wall-three-passages.mp4','omitted':'reverse moves and state fixture preparation','decode':'passed'})
(destination/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
for name in ('r4-playmode.xml','r4-lowwall-playmode.xml'):
    shutil.copy2(root/'src/Logs'/name,destination/name)
print(json.dumps(summary,ensure_ascii=False,indent=2))
