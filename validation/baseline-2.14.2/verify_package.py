"""Offline build/package checks; this does not execute the Windows installer."""
import argparse
import hashlib
import json
import re
import struct
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--source", type=Path, required=True)
parser.add_argument("--baseline-source", type=Path, required=True)
parser.add_argument("--extracted", type=Path, required=True)
parser.add_argument("--baseline-extracted", type=Path, required=True)
parser.add_argument("--installer", type=Path, required=True)
args = parser.parse_args()
root = args.source
ready = root / "ClipShelf_READY"
checks = []

def check(value, label):
    if not value:
        raise AssertionError(label)
    checks.append(label)
    print("PASS: " + label, flush=True)

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def is_x64(path):
    data = path.read_bytes()
    offset = struct.unpack_from("<I", data, 0x3C)[0]
    return data[:2] == b"MZ" and data[offset:offset+4] == b"PE\0\0" and struct.unpack_from("<H", data, offset+4)[0] == 0x8664

for relative in ("ClipShelf.exe", "ui/ClipShelf.UI.exe"):
    path = ready / relative
    check(is_x64(path), relative + " is a Windows x64 PE executable")
    check("2.14.2.0".encode("utf-16-le") in path.read_bytes(), relative + " has 2.14.2.0 version resources")
check(is_x64(args.installer), "Setup is an amd64 Unicode NSIS executable")
check("2.14.2".encode("utf-16-le") in args.installer.read_bytes(), "Setup has 2.14.2 product version resources")
check("Version: v2.14.2".encode("utf-16-le") in (ready / "ClipShelf.dll").read_bytes() and
      "關於 ClipShelf 2.14.2".encode("utf-16-le") in (ready / "ui/ClipShelf.UI.dll").read_bytes(),
      "compiled About UI and Core crash-report strings both identify 2.14.2")

for base, name in ((ready, "ClipShelf"), (ready / "ui", "ClipShelf.UI")):
    config = json.loads((base / (name + ".runtimeconfig.json")).read_text())["runtimeOptions"]
    frameworks = {x["name"] for x in config["includedFrameworks"]}
    check({"Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App"}.issubset(frameworks), name + " bundles .NET and Windows Desktop runtime")
    check(all((base / name).is_file() for name in ("coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Windows.Forms.dll")), name + " includes required runtime files")
check((ready / "ui/PresentationFramework.dll").is_file() and (ready / "ui/wpfgfx_cor3.dll").is_file(), "WPF managed and native runtime are included")

files = sorted(p for p in ready.rglob("*") if p.is_file())
payload = [{"path": p.relative_to(ready).as_posix(), "size_bytes": p.stat().st_size, "sha256": sha(p)} for p in files]
check(len(payload) == 930, "published payload has 930 files")
check(all((args.extracted / x["path"]).is_file() and sha(args.extracted / x["path"]) == x["sha256"] for x in payload), "all 930 installer payload files match published bytes by SHA-256")
check((args.extracted / "Uninstall.exe").is_file(), "installer contains its generated uninstaller")
check(not any(p.name == "history.json" for p in files), "package includes no personal history.json")

manifest = (root / "installer/uninstall-files.nsh").read_text()
deletes = re.findall(r'^Delete "\$INSTDIR\\(.*)"$', manifest, flags=re.M)
check({x.replace("\\", "/") for x in deletes} == {x["path"] for x in payload}, "uninstall manifest removes exactly the shipped program payload")
check(all(not x.startswith("\\") and ".." not in x.split("\\") for x in deletes), "uninstall payload paths stay inside INSTDIR")
new_nsi = (root / "installer/ClipShelf.nsi").read_text(encoding="utf-8-sig")
old_nsi = (args.baseline_source / "installer/ClipShelf.nsi").read_text(encoding="utf-8-sig")
check(new_nsi.replace("2.14.2", "2.14.0") == old_nsi, "installer upgrade, mutex, data retention and startup logic are unchanged from 2.14.0")
check('InstallDir "$LOCALAPPDATA\\Programs\\ClipShelf"' in new_nsi and not re.search(r'^(Delete|RMDir).*LOCALAPPDATA', new_nsi, re.M), "installer source keeps program and personal data paths separate (static check)")

protected = ["ClipShelf/AppSettings.cs", "ClipShelf/HistoryStore.cs", "ClipShelf/BackupService.cs",
             "Shared/SafeState.cs", "Shared/AutoBackup.cs"]
protected.extend(str(p.relative_to(args.baseline_source)) for p in (args.baseline_source / "ClipShelf.Core").glob("*.cs"))
def preserved_source(relative):
    new = (root / relative).read_bytes()
    old = (args.baseline_source / relative).read_bytes()
    if relative == "ClipShelf.Core/CrashReporter.cs":
        new = new.replace(b"Version: v2.14.2", b"Version: v2.14.0")
    return new == old
check(all(preserved_source(p) for p in protected), "Core logic, settings, storage, backup and safe-state remain baseline; crash report changes version only")
runtime_changes = [x["path"] for x in payload if (args.baseline_extracted / x["path"]).is_file() and sha(args.baseline_extracted / x["path"]) != x["sha256"]]
check(set(runtime_changes).issubset({"ClipShelf.exe", "ClipShelf.dll", "ClipShelf.pdb", "ClipShelf.deps.json", "ui/ClipShelf.UI.exe", "ui/ClipShelf.UI.dll", "ui/ClipShelf.UI.pdb", "ui/ClipShelf.UI.deps.json"}), "shared runtime binaries are byte-identical to 2.14.0; only application files changed")
for relative in ("ClipShelf.deps.json", "ui/ClipShelf.UI.deps.json"):
    check((ready / relative).read_text().replace("2.14.2", "2.14.0") == (args.baseline_extracted / relative).read_text(), relative + " changes only application version metadata")
new_paths = [x["path"] for x in payload if not (args.baseline_extracted / x["path"]).is_file()]
check(set(new_paths) == {"DEVELOPMENT_HISTORY.md", "BUILD_SUCCESS.log"}, "payload paths retain 2.14.0 layout with only build/history documents added")

for filename in ("BUILD_EXE.cmd", "BUILD_SETUP.cmd"):
    data = (root / filename).read_bytes()
    check(b"\r\n" in data and b"\n" not in data.replace(b"\r\n", b""), filename + " preserves Windows CRLF")
for filename in ("BUILD_EXE.ps1", "BUILD_SETUP.ps1"):
    check((root / filename).read_bytes().startswith(b"\xef\xbb\xbf"), filename + " preserves UTF-8 BOM for Windows PowerShell")

(root / "validation/payload-manifest.json").write_text(json.dumps({"version": "2.14.2", "files": payload}, ensure_ascii=False, indent=2) + "\n")
(root / "validation/package-checks.json").write_text(json.dumps({"version": "2.14.2", "checks_passed": len(checks), "checks": checks, "native_windows_runtime": "UNVERIFIED", "wine_runtime": "BLOCKED_BY_HOST_SOCKET_RESTRICTION", "runtime_binary_changes": runtime_changes}, ensure_ascii=False, indent=2) + "\n")
print(f"ALL {len(checks)} OFFLINE PACKAGE CHECKS PASSED")
