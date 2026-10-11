"""First source import: validate protected files against verified delivery hashes.
Not used when a preceding source commit exists; ordinary PRs use actual base SHA.
"""
from pathlib import Path
import json,hashlib,shutil
root=Path(__file__).resolve().parent
record=json.loads((root/'.ci/initial-baseline-sha256.json').read_text(encoding='utf-8'))
target=root/'.ci-initial-baseline'
if target.exists(): raise FileExistsError(str(target))
for entry in record['protected_files']:
    relative=Path(entry['path'])
    if relative.is_absolute() or '..' in relative.parts: raise ValueError('Unsafe baseline path')
    source=root/relative
    if hashlib.sha256(source.read_bytes()).hexdigest()!=entry['sha256']:
        raise ValueError('Initial import differs from verified data safety baseline: '+str(relative))
for entry in record['protected_files']:
    source=root/entry['path']; dest=target/entry['path'];dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,dest)
print('Initial protected baseline verified against official source SHA-256 record; this is not an earlier GitHub source commit.')
