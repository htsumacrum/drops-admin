"""Regenera manifest.json do drops-updates.
Uso: python3 make-manifest.py <pasta-drops-release> <loader_version>
Mantem overlay_version atual; recalcula sha256/size de todos os arquivos.
"""
import os, json, hashlib, sys

root, ver = sys.argv[1], sys.argv[2]
old = {}
try:
    old = json.load(open(os.path.join(root, 'manifest.json')))
except Exception:
    pass
man = {'loader_version': ver,
       'overlay_version': old.get('overlay_version', '3.1.2'),
       'files': []}
for dirpath, _, fns in os.walk(root):
    if '.git' in dirpath:
        continue
    for fn in sorted(fns):
        fp = os.path.join(dirpath, fn)
        rel = os.path.relpath(fp, root).replace(chr(92), '/')
        with open(fp, 'rb') as f:
            man['files'].append({
                'path': rel,
                'sha256': hashlib.sha256(f.read()).hexdigest(),
                'size': os.path.getsize(fp),
            })
json.dump(man, open(os.path.join(root, 'manifest.json'), 'w'), indent=1)
print('manifest:', ver, '| files:', len(man['files']))
for f in man['files']:
    if f['path'] == 'loader/loader.exe':
        print('loader.exe:', f['sha256'], f['size'])
