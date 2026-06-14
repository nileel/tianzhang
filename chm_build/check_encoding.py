import os

docs_root = r"D:\天章游戏开发\docs"
encodings = {}

for root, dirs, files in os.walk(docs_root):
    for f in files:
        if f.endswith(".txt"):
            fp = os.path.join(root, f)
            for enc in ["utf-8", "utf-8-sig", "gbk", "gb2312"]:
                try:
                    with open(fp, "r", encoding=enc) as fh:
                        fh.read(500)
                    encodings[enc] = encodings.get(enc, 0) + 1
                    break
                except:
                    continue

for k, v in sorted(encodings.items(), key=lambda x: -x[1]):
    print(f"{k}: {v} files")
print(f"Total: {sum(encodings.values())}")
