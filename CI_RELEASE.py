"""Complete the Actions artifact with provenance and actual-byte hashes."""
from pathlib import Path
import json, hashlib, os, shutil, subprocess
root=Path(__file__).resolve().parent
out=root/'out'
receipts=list(out.glob('ClipShelf-*-BuildReceipt.json'))
if len(receipts)!=1: raise RuntimeError('Expected exactly one completed build receipt')
receipt=json.loads(receipts[0].read_text(encoding='utf-8'))
version=receipt['version']
tests=(root/'validation/tests.log').read_text(encoding='utf-8')
package=json.loads((root/'validation/package-checks.json').read_text(encoding='utf-8'))
provenance={key:os.environ.get(key) for key in ('GITHUB_REPOSITORY','GITHUB_SHA','GITHUB_RUN_ID','GITHUB_RUN_ATTEMPT','GITHUB_REF','RUNNER_OS')}
provenance['SOURCE_COMMIT'] = subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip()
shutil.copy2(root/'README.txt',out/f'ClipShelf-{version}-使用說明.txt')
notes=out/f'ClipShelf-{version}-更新說明與測試結果.md'
notes.write_text(f'# ClipShelf {version} Windows Runner build\n\nBuild/Package PASS; native GUI UNVERIFIED.\n\n'+json.dumps(provenance,ensure_ascii=False,indent=2)+'\n\n'+tests+f'\nOffline package checks: {package["checks_passed"]}\n',encoding='utf-8')
files=[]
for f in sorted(out.glob('ClipShelf-*')):
    if f.is_file() and f.suffix in ('.zip','.exe','.json','.txt','.md'):
        files.append({'file_name':f.name,'size_bytes':f.stat().st_size,'sha256':hashlib.sha256(f.read_bytes()).hexdigest()})
manifest={'version':version,'build':'PASS','package':'PASS','native_gui':'UNVERIFIED','provenance':provenance,'files':files,'package_checks':package['checks_passed'],'artifact_retention_days':7,'canonical_delivery':'NOT_YET_PROMOTED','manifest_self_hash':'Recorded outside this manifest to avoid recursion'}
(out/f'ClipShelf-{version}-版本交付清單.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
