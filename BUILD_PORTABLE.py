"""Build Windows artifacts on a Windows/Linux host, without Work or Library APIs.
Requires Python 3.10+, .NET 8 SDK, NSIS 3.x (amd64 target), 7-Zip and pefile.
Always requires a separate, verified pre-patch baseline for regression checks.
"""
import argparse, hashlib, json, os, platform, shutil, subprocess, sys, zipfile
from pathlib import Path
from datetime import datetime, timezone
import xml.etree.ElementTree as ET

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--makensis', default='makensis')
    parser.add_argument('--sevenzip', default='7z')
    parser.add_argument('--nsisdir')
    parser.add_argument('--baseline', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parent
    baseline = args.baseline.resolve()
    if baseline == root or not (baseline/'ClipShelf/HistoryStore.cs').is_file():
        raise ValueError('Use a separate extracted official source as baseline.')
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=True)
    version = ET.parse(root/'ClipShelf/ClipShelf.csproj').findtext('.//Version')
    core_version = ET.parse(root/'ClipShelf.Core/ClipShelf.Core.csproj').findtext('.//Version')
    if version != core_version or ('VIProductVersion "'+version+'.0"') not in (root/'installer/ClipShelf.nsi').read_text():
        raise ValueError('Core/UI/NSIS version mismatch: update all version resources before build.')
    evidence = root/'validation'; evidence.mkdir(exist_ok=True)
    for name in ('tests.log','publish-core.log','publish-wpf.log','installer-build.log','installer-extraction.log','package-checks.log','package-checks.json','payload-manifest.json','commands.json'):
        (evidence/name).unlink(missing_ok=True)
    payload = root/'release'
    if payload.exists(): shutil.rmtree(payload)
    payload.mkdir()
    env = os.environ.copy()
    if args.nsisdir: env['NSISDIR'] = str(Path(args.nsisdir).resolve())
    commands = []
    def run(command, log):
        commands.append(command)
        (evidence/'commands.json').write_text(json.dumps(commands,ensure_ascii=False,indent=2,default=str)+'\n',encoding='utf-8')
        print('Running: '+str(command[0])+' '+str(command[1]), flush=True)
        with (evidence/log).open('w', encoding='utf-8') as stream:
            subprocess.run([str(x) for x in command], cwd=root, env=env, stdout=stream, stderr=subprocess.STDOUT, check=True)
    run([args.dotnet,'run','--project',root/'Tests/Tests.csproj','-c','Release'], 'tests.log')
    for project, target, log in [('ClipShelf.Core/ClipShelf.Core.csproj',payload,'publish-core.log'),('ClipShelf/ClipShelf.csproj',payload/'ui','publish-wpf.log')]:
        run([args.dotnet,'publish',root/project,'-c','Release','-r','win-x64','--self-contained','true','-p:EnableWindowsTargeting=true','-o',target],log)
    for name in ('README.txt','DEVELOPMENT_HISTORY.md','Diagnostics/Measure-Wake.ps1'):
        file=root/name
        if file.exists(): shutil.copy2(file,payload/file.name)
    (payload/'BUILD_SUCCESS.log').write_text(f'ClipShelf {version}\nBuild host: {platform.system()} {platform.machine()}\nCore and WPF publish PASS; Windows native interaction UNVERIFIED\n',encoding='utf-8')
    files=sorted(f for f in payload.rglob('*') if f.is_file())
    lines=['# Generated from exact published payload.']
    for f in files: lines.append('Delete "$INSTDIR\\'+str(f.relative_to(payload)).replace('/','\\').replace('$','$$')+'"')
    for d in sorted((x for x in payload.rglob('*') if x.is_dir()),key=lambda x:len(str(x)),reverse=True):
        lines.append('RMDir "$INSTDIR\\'+str(d.relative_to(payload)).replace('/','\\').replace('$','$$')+'"')
    (root/'installer/uninstall-files.nsh').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    setup=output/f'ClipShelf-{version}-Setup.exe'
    if setup.exists(): raise FileExistsError('Refusing to overwrite previous installer: '+str(setup))
    prefix='/' if platform.system()=='Windows' else '-'
    run([args.makensis,prefix+'INPUTCHARSET','UTF8',prefix+'DPAYLOAD_DIR='+str(payload),prefix+'DOUTPUT_FILE='+str(setup),root/'installer/ClipShelf.nsi'],'installer-build.log')
    extracted=output/f'installer-payload-{version}'
    if extracted.exists(): raise FileExistsError(str(extracted))
    run([args.sevenzip,'x',setup,'-o'+str(extracted),'-y'],'installer-extraction.log')
    run([sys.executable,root/'validation/verify_release.py','--source',root,'--baseline',baseline,'--setup',setup,'--extracted',extracted],'package-checks.log')
    (evidence/'commands.json').write_text(json.dumps(commands,ensure_ascii=False,indent=2,default=str)+'\n',encoding='utf-8')
    source=output/f'ClipShelf-{version}-Source.zip'
    if source.exists(): raise FileExistsError(str(source))
    with zipfile.ZipFile(source,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as archive:
        for file in sorted(root.rglob('*')):
            if not file.is_file() or any(x in ('bin','obj','release','out','ClipShelf_READY','__pycache__','.git','.ci-baseline','.ci-initial-baseline','.tools') for x in file.relative_to(root).parts): continue
            if file.suffix.lower() in ('.zip','.exe'): continue
            archive.write(file,'ClipShelf-'+version+'/'+file.relative_to(root).as_posix())
    with zipfile.ZipFile(source) as archive:
        if archive.testzip() is not None: raise ValueError('Source ZIP CRC failure')
    entries=[{'file_name':f.name,'size_bytes':f.stat().st_size,'sha256':hashlib.sha256(f.read_bytes()).hexdigest()} for f in (source,setup)]
    receipt={'version':version,'created_utc':datetime.now(timezone.utc).isoformat(),'build_host':platform.system(),'build':'PASS','package':'PASS','native_windows_interaction':'UNVERIFIED','files':entries}
    (output/f'ClipShelf-{version}-BuildReceipt.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(receipt,ensure_ascii=False,indent=2),flush=True)

if __name__=='__main__': main()
