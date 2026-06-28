import os, glob, sys

html_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'html')
count = 0

for filepath in glob.glob(os.path.join(html_dir, '*.html')):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    content = content.replace('charset=UTF-8', 'charset=GBK')
    with open(filepath, 'w', encoding='gbk', errors='replace') as f:
        f.write(content)
    count += 1

for fn in ['toc.hhc', 'project.hhp']:
    fp = os.path.join(html_dir, fn)
    if os.path.exists(fp):
        with open(fp, 'r', encoding='utf-8') as f:
            content = f.read()
        with open(fp, 'w', encoding='gbk', errors='replace') as f:
            f.write(content)
        count += 1

print(f'GBK conversion done: {count} files')
