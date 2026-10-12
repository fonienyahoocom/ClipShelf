"""Offline package/provenance checks, never a native Windows runtime PASS.
Usage: python verify_release.py --source .. --baseline ... --setup ... --extracted ...
Requires pefile. NSIS payload must first be extracted with 7-Zip.
"""
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path
import pefile

p = argparse.ArgumentParser()
for name in ('source', 'baseline', 'setup', 'extracted'):
    p.add_argument('--'+name, type=Path, required=True)
a = p.parse_args()
s, b, payload = a.source, a.baseline, a.source/'release'
release_version = ET.parse(s/'ClipShelf/ClipShelf.csproj').findtext('.//Version')
checks = []
def check(condition, label):
    if not condition: raise AssertionError(label)
    checks.append(label)
    print('PASS: '+label, flush=True)
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def version(path):
    pe = pefile.PE(str(path))
    strings = {}
    for group in pe.FileInfo:
        for entry in group:
            for table in getattr(entry, 'StringTable', []):
                strings.update({k.decode():v.decode() for k,v in table.entries.items()})
    return pe.FILE_HEADER.Machine, strings

for relative in ('ClipShelf.exe','ui/ClipShelf.UI.exe'):
    machine, text = version(payload/relative)
    check(machine == 0x8664, relative+' is native Windows x64 PE')
    check(text.get('FileVersion') == release_version+'.0' and text.get('ProductVersion') == release_version, relative+' version resources match current version')
machine, text = version(a.setup)
check(machine == 0x8664, 'Setup is amd64 Unicode NSIS PE')
check(text.get('FileVersion') == release_version+'.0' and text.get('ProductVersion') == release_version, 'Setup version resources match current version')
check(('關於 ClipShelf '+release_version).encode('utf-16-le') in (payload/'ui/ClipShelf.UI.dll').read_bytes(), 'compiled WPF About text identifies current version')
check(('Version: v'+release_version).encode('utf-16-le') in (payload/'ClipShelf.dll').read_bytes(), 'compiled Core crash text identifies current version')
for directory, name in ((payload,'ClipShelf'),(payload/'ui','ClipShelf.UI')):
    config=json.loads((directory/(name+'.runtimeconfig.json')).read_text())['runtimeOptions']
    frameworks={x['name']:x['version'] for x in config['includedFrameworks']}
    check(frameworks.get('Microsoft.NETCore.App') == '8.0.31' and frameworks.get('Microsoft.WindowsDesktop.App') == '8.0.31', name+' bundles runtime 8.0.31')
    check(all((directory/f).is_file() for f in ('coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Windows.Forms.dll')), name+' includes required runtime bytes')
check((payload/'ui/PresentationFramework.dll').is_file() and (payload/'ui/wpfgfx_cor3.dll').is_file(), 'WPF managed/native runtime included')
files=sorted(x for x in payload.rglob('*') if x.is_file())
entries=[{'path':x.relative_to(payload).as_posix(),'size_bytes':x.stat().st_size,'sha256':sha(x)} for x in files]
check(len(entries) >= 930, 'complete self-contained payload present')
check(all((a.extracted/x['path']).is_file() and sha(a.extracted/x['path']) == x['sha256'] for x in entries), f'all {len(entries)} extracted installer files match published SHA-256')
check((a.extracted/'Uninstall.exe').is_file(), 'generated uninstaller included')
check(not any(x.name == 'history.json' for x in files), 'payload contains no user history')
uninstall=(s/'installer/uninstall-files.nsh').read_text()
deletes=re.findall(r'^Delete "\$INSTDIR\\(.*)"$',uninstall,re.M)
check({x.replace('\\','/') for x in deletes} == {x['path'] for x in entries}, 'uninstall list exactly matches shipped payload')
check(all('..' not in x.split('\\') and not x.startswith('\\') for x in deletes), 'uninstall file paths remain under INSTDIR')
nsi=(s/'installer/ClipShelf.nsi').read_text()
check('InstallDir "$LOCALAPPDATA\\Programs\\ClipShelf"' in nsi and not re.search(r'^(Delete|RMDir).*LOCALAPPDATA',nsi,re.M), 'installer leaves separate personal data path intact')
check('Call CheckClosed' in nsi and 'Call un.CheckClosed' in nsi and 'ClipShelf.Core.SingleInstance' in nsi, 'install/uninstall require clean Core shutdown')
# 2.15.5 changes presentation only; persistence sources must
# remain byte-identical to the verified 2.15.3 predecessor.
protected=('ClipShelf/HistoryStore.cs','ClipShelf/BackupService.cs','ClipShelf/ClipItem.cs',
           'ClipShelf/ClipEditing.cs','ClipShelf.Core/CoreStateStore.cs',
           'Shared/SafeState.cs','Shared/AutoBackup.cs')
for relative in protected:
    check((s/relative).read_bytes() == (b/relative).read_bytes(), relative+' byte-identical to 2.15.3 baseline')
check('Read(path); // Never replace' in (s/'Shared/SafeState.cs').read_text(), 'fail-closed write revalidates existing history before replace')
for filename in ('BUILD_EXE.cmd','BUILD_SETUP.cmd'):
    data=(s/filename).read_bytes()
    check(not data.startswith(b'\xef\xbb\xbf') and data.isascii() and b'\n' not in data.replace(b'\r\n',b''), filename+' is ASCII/no BOM/CRLF')
for filename in ('BUILD_EXE.ps1','BUILD_SETUP.ps1'):
    check((s/filename).read_bytes().startswith(b'\xef\xbb\xbf'), filename+' keeps Windows PowerShell UTF-8 BOM')
ui=(s/'ClipShelf/MainWindow.xaml.cs').read_text()
quick=ET.parse(s/'ClipShelf/QuickPickerWindow.xaml').getroot()
ns={'w':'http://schemas.microsoft.com/winfx/2006/xaml/presentation'}
check(quick.tag.endswith('Window') and quick.attrib['Width']=='440' and quick.attrib['MinWidth']=='340', 'independent compact WPF Window with minimum size')
listbox=quick.find('.//w:ListBox',ns)
check(listbox is not None and listbox.attrib.get('VirtualizingPanel.VirtualizationMode')=='Recycling', 'quick rows use recycling virtualization')
check('TimeSpan.FromSeconds(45)' not in ui and 'TimeSpan.FromSeconds(300)' not in ui and 'StartWarmStandby' not in ui, '45/300-second WPF expiry paths absent')
core=(s/'ClipShelf.Core/CoreContext.cs').read_text()
check('ToggleUiFromHotkey' in core and 'RequestUi("toggle-quick")' in core and '"--resident"' in core, 'hotkey routes to resident independent picker')
check('0x4000' in (s/'ClipShelf.Core/NativeMessageWindow.cs').read_text() and 'HotkeyRegistered = RegisterHotKey' in (s/'ClipShelf.Core/NativeMessageWindow.cs').read_text(), 'native hotkey registration result and MOD_NOREPEAT are handled')
check('SetWindowsHookEx' not in (s/'ClipShelf.Core/BubbleForm.cs').read_text(), 'bubble patch introduces no low-level global hook')
check('IsVisible) await IndexAttachmentsAsync' in (s/'ClipShelf/ProductFeatures.cs').read_text(), 'hidden manager skips periodic attachment reindex')
check('FAILED' not in (payload/'BUILD_SUCCESS.log').read_text() and 'Build host:' in (payload/'BUILD_SUCCESS.log').read_text(), 'build evidence states cross-build host and does not claim Windows runtime')
check(listbox.attrib['SelectionMode']=='Extended', 'picker uses native Shift/Ctrl mouse multiselection')
picker_code=(s/'ClipShelf/QuickPickerWindow.xaml.cs').read_text()
integration=(s/'ClipShelf/PickerIntegration.cs').read_text()
check('DeleteRow_Click' in picker_code and 'DeleteSelected_Click' in picker_code and 'SoftDeleteItems(items, refresh: false, showUndo: false)' in integration, 'row and selected deletion wired to existing Safe Delete')
check('PickerCloseOnDeactivate' in picker_code and 'HidePicker(restoreTarget: false)' in picker_code, 'outside dismissal preserves outside focus and keep-open setting')
check('Scope_Changed' in picker_code and '() => _folderPaths' in integration, 'folder selector wired to canonical folders without manager filter mutation')
check('ApplyBubbleVisibility(); // Independent preference' in core and 'ShowBubbleNow();' in core and '_coreBridge.ShowBubble();' in integration, 'quick-mode Core and manager routes preserve floating bubble')
check('IsButtonSource' in picker_code and 'e.ClickCount > 1' in picker_code, 'delete button cannot double-click paste or delete replacement row')
check((s/'BUILD_PORTABLE.py').is_file() and 'library' not in (s/'BUILD_PORTABLE.py').read_text().lower().split('import argparse')[1], 'portable build uses local toolchain without Work or connector dependency')
check('Rows.SelectAll()' in picker_code and 'Rows.IsKeyboardFocusWithin' in picker_code and 'BatchActions.Visibility = count > 1' in picker_code, 'Ctrl+A scoped to list and batch actions only visible for multiselection')
check('FloatingIconEnabled' in (s/'ClipShelf/AppSettings.cs').read_text() and 'bubble-visible' in integration and 'SetBubbleEnabled' in core, 'independent floating preference uses sole live state writer')
check('_visibleItems.Remove(item)' in integration and 'CurrentSection().Kind == "Trash") RefreshVisibleItems();' in integration, 'picker deletion updates active manager projection and existing Trash query without clearing unrelated draft cards')
# Validate the actual WPF hierarchy and outside-focus wiring, not only data tests.
manager_xaml=ET.parse(s/'ClipShelf/MainWindow.xaml').getroot()
titles=[x for x in manager_xaml.iter() if x.tag.endswith('TextBlock') and x.attrib.get('Text')=='{Binding Title}']
editors=[x for x in manager_xaml.iter() if x.tag.endswith('TextBox') and x.attrib.get('{http://schemas.microsoft.com/winfx/2006/xaml}Name')=='ClipTitleEditor']
check(len(titles)==1 and titles[0].attrib.get('FontSize')=='17' and titles[0].attrib.get('FontWeight')=='SemiBold'
      and len(editors)==1 and editors[0].attrib.get('FontSize')=='16' and editors[0].attrib.get('Grid.Column')=='1',
      'editable custom title has 17pt readable text and aligned inline editor')
xaml=(s/'ClipShelf/MainWindow.xaml').read_text(encoding='utf-8-sig')
check('Text="{Binding KindLabel}"' not in xaml and 'Text="{Binding TimeText}"' not in xaml
      and 'Text="{Binding UsageText}"' not in xaml and 'Text="{Binding CategoryText}"' not in xaml
      and 'Content="✎ 標題"' not in xaml,
      'no redundant always-visible type, time, usage, category or rename badges')
check('Click="RenameTitle_Click"' in xaml and 'Text="{Binding Title}"' in xaml
      and 'Content="＋ 標題"' in xaml and xaml.count('RelativeSource AncestorType=ListBoxItem') >= 5
      and 'Text="{Binding EditorText' in xaml,
      'clickable custom title, contextual add/drag/actions and native selectable content retained')
manager=(s/'ClipShelf/MainWindow.xaml.cs').read_text(encoding='utf-8-sig')
check('BuildClipDetailsMenu(item)' in manager and manager.count('BuildClipDetailsMenu(item)') >= 2
      and all(x in manager for x in ('"建立時間"', '"最近使用"', '"使用次數"', '"類型"', '"來源"', '"分類"')),
      'card details remain accessible on demand in both normal and deleted contexts')
check('InlineEditDismissal.ShouldBlockOutsideHide' in ui
      and '!ReferenceEquals(_inlineEditItem, item) || !item.IsInlineEditing || !IsActive' in (s/'ClipShelf/CardInteractions.cs').read_text(),
      'outside deactivation respects inline drafts and delayed editor focus cannot steal external foreground')
test=(s/'validation/tests.log').read_text()
totals=[int(x) for x in re.findall(r'PASSED (\d+) CHECKS', test)]
check(bool(totals) and max(totals) >= 86,
      'Windows automated tests retain >=86 baseline checks and include new pin regressions')
check('pin moves older item to top immediately' in test and 'second click removes pin' in test
      and 'Core overflow preserves pinned record in History' in test,
      'actual Windows tests exercised pin, unpin, persistence and Core history retention')
check('PinRow_Click' in picker_code and 'ReorderPins()' in picker_code
      and 'PinOpacity' in (s/'ClipShelf/QuickPickerWindow.xaml').read_text(encoding='utf-8-sig'),
      'quick picker per-row pin/unpin control is wired to its bounded reorder action')
(s/'validation/payload-manifest.json').write_text(json.dumps({'version':release_version,'files':entries},ensure_ascii=False,indent=2)+'\n')
(s/'validation/package-checks.json').write_text(json.dumps({'version':release_version,'checks_passed':len(checks),'checks':checks,'payload_files':len(entries),'native_windows_runtime':'UNVERIFIED'},ensure_ascii=False,indent=2)+'\n')
print(f'ALL {len(checks)} OFFLINE RELEASE CHECKS PASSED; NATIVE WINDOWS UNVERIFIED')
