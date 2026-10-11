import os, pathlib, subprocess, hashlib, json
root=pathlib.Path('/workspace/scratch/bbc0cb45bfaf')
prefix=pathlib.Path('/tmp/clipshelf-test-win')
local=prefix/'drive_c/users/root/AppData/Local'
install=local/'Programs/ClipShelf'
data=local/'ClipShelf'
env=os.environ|{'DISPLAY':':91','WINEPREFIX':str(prefix),'WINEARCH':'win64','WINEDEBUG':'-all'}
log=[]
def check(v,s):
 assert v,s
 log.append('PASS: '+s);print(log[-1],flush=True)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
payload=root/'clipshelf-work/release'
files=[p for p in payload.rglob('*') if p.is_file()]
check(all((install/p.relative_to(payload)).is_file() and sha(p)==sha(install/p.relative_to(payload)) for p in files),f'fresh install matches all {len(files)} payload files')
check((install/'Uninstall.exe').is_file(),'uninstaller created')
history=data/'history.json';before=sha(history)
(data/'user-sentinel.txt').write_text('retain personal files 測試')
(install/'user-added.txt').write_text('foreign file must survive uninstall')
res=subprocess.run(['wine',str(root/'deliverables/ClipShelf-2.14.0-Setup.exe'),'/S'],env=env,timeout=180)
check(res.returncode==0,'in-place upgrade exits successfully')
check(sha(history)==before and (data/'user-sentinel.txt').read_text()=='retain personal files 測試','upgrade preserves history bytes and user data')
check(all(sha(p)==sha(install/p.relative_to(payload)) for p in files),'upgraded files match release payload')
# _?= suppresses self-copy so process completion means uninstall completed.
res=subprocess.run(['wine',str(install/'Uninstall.exe'),'/S','_?=C:\\users\\root\\AppData\\Local\\Programs\\ClipShelf'],env=env,timeout=120)
check(res.returncode==0,'uninstall exits successfully')
check(all(not (install/p.relative_to(payload)).exists() for p in files),'uninstall removes all exact shipped payload files')
check((install/'user-added.txt').is_file(),'uninstall preserves unrelated file in program directory')
check(sha(history)==before and (data/'user-sentinel.txt').is_file(),'uninstall preserves personal history and user data')
reg=subprocess.run(['wine','reg','query',r'HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipShelf'],env=env,capture_output=True,timeout=45)
check(reg.returncode!=0,'uninstall removes Add/Remove Programs entry')
log.insert(0,'PASS: running Core mutex blocks silent install with exit code 2 (preceding command)')
(root/'clipshelf-work/validation/installer-tests.log').write_text('\n'.join(log)+'\n',encoding='utf-8')
