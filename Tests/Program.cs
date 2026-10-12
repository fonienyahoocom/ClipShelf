using ClipShelf;
using ClipShelfCore;
using ClipShelfShared;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;
var root = Path.Combine(Path.GetTempPath(), "ClipShelfTests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
Console.WriteLine("CHAT_BUILD_E2E: ClipShelf 2.15.3 pin/unpin Quick Picker regression");
void Check(bool value, string label) { if (!value) throw new Exception("FAIL: "+label); Console.WriteLine("PASS: "+label); checks++; }
string State(IEnumerable<ClipItem> items, AppSettings? settings = null) => JsonSerializer.Serialize(new { Items=items.ToList(), Categories = new[]{"AI 用語/測試"}, Settings=settings??new AppSettings { HistoryModelVersion=1 } });
try {
    var dir=Path.Combine(root,"state"); Directory.CreateDirectory(dir); var path=Path.Combine(dir,"history.json");
    File.WriteAllText(path,"{broken");
    bool rejected=false;try { new CoreStateStore(dir); } catch { rejected=true; }
    Check(rejected && File.ReadAllText(path)=="{broken", "corrupt startup preserves bytes and fails closed");
    rejected=false;try { SafeState.Write(path,State(Array.Empty<ClipItem>())); } catch { rejected=true; }
    Check(rejected && File.ReadAllText(path)=="{broken", "write cannot replace unreadable state");
    File.Delete(path);
    var deleted=new ClipItem {Text="同一段",ContentHash=CoreStateStore.Sha256("T|同一段"),IsDeleted=true,DeletedAt=DateTime.Now,HistoryVisible=true};
    SafeState.Write(path,State(new[]{deleted})); var store=new CoreStateStore(dir); store.AddOrTouchText("同一段","test","");
    var state=JsonNode.Parse(File.ReadAllText(path))!; var rows=state["Items"]!.AsArray();
    Check(rows.Count==2 && rows.Count(x=>x?["IsDeleted"]?.GetValue<bool>()!=true)==1,"recapture deleted text creates visible active item");
    var imagePath=Path.Combine(dir,"image.png");File.WriteAllBytes(imagePath,new byte[]{1,2,3,4});
    var old=new ClipItem {Kind="Image",ImagePath=imagePath,ManagedByClipShelf=true,HistoryVisible=true,LastUsedAt=DateTime.Now.AddDays(-2)};
    var saved=new ClipItem {Text="收藏",Favorite=true,HistoryVisible=true,LastUsedAt=DateTime.Now.AddDays(-1)};
    SafeState.Write(path,State(new[]{old,saved},new AppSettings {HistoryModelVersion=1,MaxRegularItems=1}));store.Reload();store.AddOrTouchText("新段落","test","");
    rows=JsonNode.Parse(File.ReadAllText(path))!["Items"]!.AsArray();
    Check(rows.First(x=>x!["Id"]!.GetValue<Guid>()==old.Id)!["IsDeleted"]!.GetValue<bool>() && File.Exists(imagePath),"background overflow soft deletes and retains attachment");
    Check(rows.First(x=>x!["Id"]!.GetValue<Guid>()==saved.Id)!["Favorite"]!.GetValue<bool>() && rows.First(x=>x!["Id"]!.GetValue<Guid>()==saved.Id)!["IsDeleted"]!.GetValue<bool>()==false,"overflow keeps favorites");
    var note=new ClipItem { Text="正文 {{語言}}",Title="標題",Note="備註",Category="AI 用語/測試",ContentHash=CoreStateStore.Sha256("T|正文 {{語言}}") };
    var file=Path.Combine(dir,"sample.md");File.WriteAllText(file,"# 中文\n備份驗證");var attachment=new ClipItem {Kind="Files",Files=new(){file},ManagedByClipShelf=true};
    var export=Path.Combine(root,"test.clipshelfbackup");var items=new[]{note,attachment,old};
    var result=BackupService.Export(export,items,new[]{"AI 用語/測試"},new AppSettings());
    Check(result.Warnings.Count==0 && result.PayloadCount==2,"manual backup includes attachments");
    File.Delete(file);File.Delete(imagePath);
    var preview=BackupService.AnalyzeImport(export,Array.Empty<ClipItem>(),Array.Empty<string>());
    var imported=BackupService.Import(export,Path.Combine(root,"restored"),preview.CertainNewItemIds.Concat(preview.UncertainItemIds).ToArray(),Array.Empty<Guid>());
    Check(imported.Items.Count==3 && imported.Items.Any(x=>x.Title=="標題" && x.Note=="備註" && x.Category=="AI 用語/測試"),"controlled restore preserves metadata and classification");
    Check(File.ReadAllText(imported.Items.Single(x=>x.Kind=="Files").Files[0])=="# 中文\n備份驗證","restore recreates missing file with exact text");
    Check(File.ReadAllBytes(imported.Items.Single(x=>x.Kind=="Image").ImagePath!).SequenceEqual(new byte[]{1,2,3,4}),"restore recreates missing image with exact bytes");
    var second=BackupService.AnalyzeImport(export,imported.Items,imported.FolderPaths);
    Check(second.NewItemCount==0 && second.DuplicateIdCount==3,"repeat import deduplicates IDs");
    SafeState.Write(path,State(imported.Items));AutoBackup.Run(dir);
    var auto=Directory.GetFiles(Path.Combine(dir,"auto-backups"),"*.zip").Single();
    var autoPreview=BackupService.AnalyzeImport(auto,Array.Empty<ClipItem>(),Array.Empty<string>());
    var autoRestore=BackupService.Import(auto,Path.Combine(root,"auto-restored"),autoPreview.CertainNewItemIds.Concat(autoPreview.UncertainItemIds).ToArray(),Array.Empty<Guid>());
    Check(autoRestore.Items.Count==3 && autoRestore.Warnings.Count==0,"automatic backup accepted by normal import and restores all items");
    Check(autoRestore.Items.Any(x=>x.Note=="備註"),"automatic backup retains new metadata");
    SafeState.Write(path,State(Array.Empty<ClipItem>(),new AppSettings {HistoryModelVersion=1,CapturePaused=true,CaptureResumeAt=DateTime.Now.AddMinutes(-1)})); store.Reload();
    Check(!store.CapturePaused,"timed pause resumes after deadline in background");
    Check(File.Exists(path+".previous") && Directory.GetFiles(Path.Combine(dir,"backups"),"*.json").Length>0,"atomic saves retain previous state and rolling snapshot");
    // v2.14.1: run the production edit service and model, not a copied UI algorithm.
    var edit=new ClipItem { Text="原始正文", Title="自訂標題", Note="備註保留", Category="AI 用語/測試", Favorite=true,
        FavoriteFolder="收藏子層", IsTemporary=true, TemporaryFolder="暫存子層", HistoryVisible=true,
        UseCount=4, ContentHash=CoreStateStore.Sha256("T|原始正文"), LastUsedAt=DateTime.Now.AddDays(-1) };
    var editId=edit.Id; var editDate=edit.LastUsedAt; var created=edit.CreatedAt;
    var notices=new List<string?>(); edit.PropertyChanged+=(_,e)=>notices.Add(e.PropertyName);
    edit.Compact=true;
    Check(notices.Contains("Compact") && notices.Contains("ContentMaxHeight") && edit.ContentMaxHeight==38,"compact view notifies existing card and preview height");
    notices.Clear();edit.Compact=true;
    Check(notices.Count==0,"unchanged compact view emits no redundant notifications");
    edit.IsContentExpanded=true;edit.Compact=false;edit.Compact=true;
    Check(edit.ContentMaxHeight==480,"expanded long text stays expanded across A/B switch");
    edit.IsContentExpanded=false;edit.DraftText="未儲存的草稿";edit.IsEditingText=true;
    Check(edit.Text=="原始正文" && edit.EditorText=="未儲存的草稿" && edit.ContentMaxHeight==480,"inline draft stays separate from saved body and can show multiline text");
    var editJson=JsonSerializer.Serialize(edit);var restoredEdit=JsonSerializer.Deserialize<ClipItem>(editJson)!;
    Check(restoredEdit.Text=="原始正文" && !editJson.Contains("DraftText") && !editJson.Contains("IsEditingText") && !editJson.Contains("Compact") && !restoredEdit.IsInlineEditing,"background serialization excludes draft and card-only presentation state");
    edit.IsEditingText=false;edit.DraftText="";
    Check(edit.Text=="原始正文" && edit.EditorText=="原始正文" && edit.UseCount==4,"cancel draft keeps original text and copy count");
    var editPath=Path.Combine(root,"edit","history.json");
    void PersistEdit()=>SafeState.Write(editPath,State(new[]{edit},new AppSettings {HistoryModelVersion=1,CompactView=true}));
    ClipEditing.SaveText(edit,"修改後的中文正文\n第二行 {{欄位}}",new[]{edit},PersistEdit);
    Check(edit.Id==editId && edit.Title=="自訂標題" && edit.Note=="備註保留" && edit.Category=="AI 用語/測試" && edit.Favorite && edit.FavoriteFolder=="收藏子層" && edit.IsTemporary && edit.TemporaryFolder=="暫存子層" && edit.LastUsedAt==editDate && edit.CreatedAt==created,"body edit preserves identity, manual title, placement and dates");
    Check(edit.ContentHash==CoreStateStore.Sha256("T|"+edit.Text) && edit.UseCount==1,"changed body gets canonical content hash and starts new repeat count");
    var onDisk=JsonNode.Parse(File.ReadAllText(editPath))!;
    Check(onDisk["Items"]![0]!["Text"]!.GetValue<string>()==edit.Text && onDisk["Settings"]!["CompactView"]!.GetValue<bool>(),"checked persistence stores changed body and existing compact setting");
    var editCore=new CoreStateStore(Path.GetDirectoryName(editPath));editCore.AddOrTouchText(edit.Text,"test","");
    onDisk=JsonNode.Parse(File.ReadAllText(editPath))!;
    Check(onDisk["Items"]!.AsArray().Count==1 && onDisk["Items"]![0]!["UseCount"]!.GetValue<int>()==2,"background recapture deduplicates edited content and increments new count");
    editCore.AddOrTouchText("原始正文","test","");
    Check(JsonNode.Parse(File.ReadAllText(editPath))!["Items"]!.AsArray().Count==2,"recapture of old content creates its own record after edit");
    edit.UseCount=4;ClipEditing.SaveText(edit,edit.Text,new[]{edit},PersistEdit);
    Check(edit.UseCount==4,"saving an unchanged body preserves repeat count");
    var other=new ClipItem {Text="另一筆",ContentHash=CoreStateStore.Sha256("T|另一筆")};
    var beforeBody=edit.Text;var beforeHash=edit.ContentHash;rejected=false;
    try { ClipEditing.SaveText(edit,other.Text,new[]{edit,other},PersistEdit); } catch(InvalidOperationException) { rejected=true; }
    Check(rejected && edit.Text==beforeBody && edit.ContentHash==beforeHash && edit.UseCount==4 && other.Text=="另一筆","edit collision is rejected without silently merging records or changing counts");
    rejected=false;try { ClipEditing.SaveText(edit,"  \n",new[]{edit},PersistEdit); } catch(InvalidOperationException) {rejected=true;}
    Check(rejected && edit.Text==beforeBody,"empty-body edit is rejected without deleting the record");
    rejected=false;try { ClipEditing.SaveText(edit,"保存失敗測試",new[]{edit},()=>throw new IOException("fixture write failure")); } catch(IOException) {rejected=true;}
    Check(rejected && edit.Text==beforeBody && edit.ContentHash==beforeHash && edit.UseCount==4,"failed checked body write rolls back all changed model fields");
    var keepText=edit.Text;var keepHash=edit.ContentHash;
    ClipEditing.SaveTitle(edit,"  我的中文卡片名稱  ",PersistEdit);
    Check(edit.Title=="我的中文卡片名稱" && edit.CardHeading==edit.Title && edit.Text==keepText && edit.ContentHash==keepHash && edit.UseCount==4 && edit.LastUsedAt==editDate,"inline rename changes only the existing Title field");
    restoredEdit=JsonSerializer.Deserialize<ClipItem>(JsonNode.Parse(File.ReadAllText(editPath))!["Items"]![0]!.ToJsonString())!;
    Check(restoredEdit.Title=="我的中文卡片名稱" && restoredEdit.Text==keepText,"renamed title and body survive a real disk reload");
    rejected=false;try {ClipEditing.SaveTitle(edit,"未保存名稱",()=>throw new IOException("fixture write failure"));} catch(IOException) {rejected=true;}
    Check(rejected && edit.Title=="我的中文卡片名稱","failed checked title write retains the original title");
    ClipEditing.SaveTitle(edit,"  ",PersistEdit);
    Check(edit.Title=="" && edit.CardHeading.StartsWith("修改後的中文正文") && edit.Text==keepText,"blank title restores automatic heading without clearing body");
    other.IsDeleted=true;other.DeletedAt=DateTime.Now;
    ClipEditing.SaveText(edit,other.Text,new[]{edit,other},PersistEdit);
    Check(edit.Text==other.Text && other.IsDeleted,"deleted duplicate does not block an active edit or revive deleted data");
    edit.IsDeleted=true;rejected=false;try {ClipEditing.SaveTitle(edit,"不得改名",PersistEdit);} catch(InvalidOperationException) {rejected=true;}
    Check(rejected && edit.Title=="","deleted record cannot be renamed until restored");edit.IsDeleted=false;
    var mode=new AppSettings {CompactView=true};
    Check(!new AppSettings().CompactView && mode.Clone().CompactView && JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(mode))!.CompactView,"new installs default A and existing B setting survives clone and reload");
    // v2.14.2: production binding state used by native Delete/typing entry.
    notices.Clear();edit.DraftText=edit.Text;edit.IsEditingText=true;
    Check(!edit.IsTextReadOnly && notices.Contains(nameof(ClipItem.IsTextReadOnly)),"entering body edit notifies the direct native TextBox read-only binding");
    notices.Clear();edit.IsEditingText=true;edit.DraftText=edit.DraftText;
    Check(notices.Count==0,"unchanged editor state and binding echo emit no redundant target updates");
    var nativeDraftOriginal=edit.Text;edit.EditorText="刪除後剩餘文字";
    Check(edit.DraftText=="刪除後剩餘文字" && edit.Text==nativeDraftOriginal && !JsonSerializer.Serialize(edit).Contains("IsTextReadOnly"),"native edit binding stores a draft without changing or serializing the original body");
    edit.EditorText=nativeDraftOriginal;edit.IsEditingText=false;
    Check(edit.IsTextReadOnly && edit.EditorText==nativeDraftOriginal && edit.Text==nativeDraftOriginal,"ending editing restores read-only content and original body");
    var pickerItems = new[] {
        new ClipItem { Text="第一筆 Alpha 中文", Title="手動標題", HistoryVisible=true, LastUsedAt=DateTime.Now },
        new ClipItem { Text="第二筆 beta", HistoryVisible=true, LastUsedAt=DateTime.Now.AddMinutes(-1), DraftText="管理草稿", IsEditingText=true, IsMarked=true, Compact=true },
        new ClipItem { Kind="Files", Files=new(){"C:/資料/說明.md"}, HistoryVisible=true, LastUsedAt=DateTime.Now.AddMinutes(-2) },
        new ClipItem { Kind="Image", ImagePath="image.png", HistoryVisible=true, LastUsedAt=DateTime.Now.AddMinutes(-3) },
        new ClipItem { Text="已刪除", HistoryVisible=true, IsDeleted=true },
        new ClipItem { Text="待清除", HistoryVisible=true, IsPendingPurge=true },
        new ClipItem { Text="純分類", HistoryVisible=false }
    };
    var picker = new QuickPickerModel(); picker.Open(pickerItems);
    Check(picker.Search("").Count==4, "picker uses real recent history and excludes deleted/pending/non-history records");
    Check(picker.Search("").First().Id==pickerItems[0].Id, "picker opens newest first");
    Check(picker.Search("ALPHA 中文").Single().Id==pickerItems[0].Id, "picker multilingual case-insensitive AND search");
    Check(picker.Search("手動標題").Single().Id==pickerItems[0].Id, "picker custom title searchable");
    Check(picker.Search("說明.md").Single().Kind=="Files", "picker file paths searchable");
    Check(picker.Search("未存在").Count==0, "picker empty search results are safe");
    var orderBefore=picker.Search("").Select(x=>x.Id).ToArray();
    pickerItems[1].LastUsedAt=DateTime.Now.AddMinutes(2);pickerItems[1].UseCount++;
    picker.Synchronize(pickerItems);
    Check(picker.Search("").Select(x=>x.Id).SequenceEqual(orderBefore), "usage updates do not reorder an open picker");
    Check(pickerItems[1].DraftText=="管理草稿" && pickerItems[1].IsEditingText && pickerItems[1].IsMarked && pickerItems[1].Compact, "picker navigation and search preserve manager drafts, selection and A/B state");
    var incomingPicker = new ClipItem { Text="新擷取",HistoryVisible=true,LastUsedAt=DateTime.Now.AddMinutes(3) };
    picker.Synchronize(pickerItems.Append(incomingPicker));
    Check(picker.Search("").Last().Id==incomingPicker.Id && picker.Search("").Take(4).Select(x=>x.Id).SequenceEqual(orderBefore), "live capture appended without moving existing selected rows");
    pickerItems[0].IsDeleted=true;picker.Synchronize(pickerItems.Append(incomingPicker));
    Check(!picker.Search("").Any(x=>x.Id==pickerItems[0].Id), "live deletion removes unavailable picker row");
    picker.Open(pickerItems.Append(incomingPicker));
    Check(picker.Search("").First().Id==incomingPicker.Id, "reopen refreshes recent ordering");
    var many=Enumerable.Range(0,12000).Select(i=>new ClipItem {Text="entry "+i,HistoryVisible=true}).ToList();picker.Open(many);
    Check(picker.Search("").Count==1000, "large history projection bounded at 1000 results");
    Check(picker.Search("entry 11999").Single().Text=="entry 11999", "search reaches records beyond displayed projection");
    // Pin is independent of Favorite and only an explicit pin change reorders the open picker.
    var pinOld = new ClipItem {Text="舊的重要內容", HistoryVisible=true, LastUsedAt=DateTime.Now.AddDays(-7)};
    var pinNew = new ClipItem {Text="新內容", HistoryVisible=true, LastUsedAt=DateTime.Now};
    picker.Open(new[]{pinOld,pinNew});
    Check(picker.Search("").First()==pinNew, "unpinned picker keeps recent-first order");
    Check(PickerPinning.Toggle(pinOld,()=>{}) && pinOld.IsPinned && !pinOld.Favorite, "pin is independent of favorite");
    picker.ReorderPins();
    Check(picker.Search("").First()==pinOld, "pin moves older item to top immediately");
    var pinPath=Path.Combine(root,"pinned","history.json");
    SafeState.Write(pinPath,State(new[]{pinOld,pinNew}));
    var persistedPin=JsonNode.Parse(File.ReadAllText(pinPath))!["Items"]![0]!;
    Check(persistedPin["PinnedAt"]!=null && persistedPin["Favorite"]!.GetValue<bool>()==false, "pin metadata persists without changing favorite");
    var reloadedPin=JsonSerializer.Deserialize<ClipItem>(persistedPin.ToJsonString())!;
    Check(reloadedPin.IsPinned && reloadedPin.Id==pinOld.Id && reloadedPin.Text==pinOld.Text, "pinned item survives disk reload");
    Check(!PickerPinning.Toggle(pinOld,()=>{}) && !pinOld.IsPinned, "second click removes pin");
    picker.ReorderPins();
    Check(picker.Search("").First()==pinNew, "unpin restores recency ordering");
    var pinFailed=false;
    try { PickerPinning.Toggle(pinOld,()=>throw new IOException("disk failure")); } catch(IOException) {pinFailed=true;}
    Check(pinFailed && !pinOld.IsPinned, "checked persistence failure rolls pin back");
    pinOld.PinnedAt=DateTime.Now;pinOld.IsDeleted=true;
    picker.Synchronize(new[]{pinOld,pinNew});
    Check(picker.Search("").Single()==pinNew, "pinned soft-deleted item hidden from picker");
    // Core background trimming must preserve pinned history items even over the configured cap.
    var corePinDir=Path.Combine(root,"core-pin");Directory.CreateDirectory(corePinDir);
    var corePin=new ClipItem {Text="保留釘選",HistoryVisible=true,PinnedAt=DateTime.Now.AddHours(-1),LastUsedAt=DateTime.Now.AddDays(-30)};
    var coreOld=new ClipItem {Text="舊的一般紀錄",HistoryVisible=true,LastUsedAt=DateTime.Now.AddDays(-2)};
    SafeState.Write(Path.Combine(corePinDir,"history.json"),State(new[]{corePin,coreOld},new AppSettings {HistoryModelVersion=1,MaxRegularItems=1}));
    var corePinStore=new CoreStateStore(corePinDir);corePinStore.AddOrTouchText("新擷取", "test", "");
    var corePinRows=JsonNode.Parse(File.ReadAllText(Path.Combine(corePinDir,"history.json")))!["Items"]!.AsArray();
    var protectedPin=corePinRows.Single(x=>x!["Id"]!.GetValue<Guid>()==corePin.Id)!;
    Check(protectedPin["IsDeleted"]!.GetValue<bool>()==false && protectedPin["HistoryVisible"]!.GetValue<bool>(), "Core overflow preserves pinned record in History");
    Check(corePinRows.Count(x=>x!["IsDeleted"]!.GetValue<bool>()==false && x!["PinnedAt"]==null && x!["HistoryVisible"]!.GetValue<bool>())==1, "Core cap continues applying to ordinary unpinned history");
    var session=new PickerSession();
    Check(!session.TryBeginPaste(), "hidden picker cannot paste");session.Open(123,456);
    Check(session.Target==123 && session.TargetPid==456 && session.Visible, "session retains original target with process identity");
    Check(session.TryBeginPaste() && !session.TryBeginPaste(), "repeated Enter cannot start duplicate paste");
    session.EndPaste();session.Hide();
    Check(!session.Visible && !session.TryBeginPaste(), "Esc/hide invalidates paste entry without clipboard operation");
    session.Open(789,321);Check(session.Target==789 && session.TargetPid==321 && session.TryBeginPaste(),"reopen accepts a fresh target after previous paste");session.EndPaste();
    var pickerSettings=new AppSettings {PickerWidth=456,PickerHeight=601,PickerLeft=-200,PickerTop=40,HasPickerPlacement=true,PickerHotkeyModifiers=3,PickerHotkeyKey=86,CompactView=true};
    var pickerClone=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(pickerSettings.Clone()))!;
    Check(pickerClone.PickerWidth==456 && pickerClone.PickerHeight==601 && pickerClone.PickerLeft==-200 && pickerClone.HasPickerPlacement && pickerClone.CompactView,"picker geometry persisted alongside unchanged A/B setting");
    Check(pickerClone.PickerHotkeyModifiers==3 && pickerClone.PickerHotkeyKey==86,"alternative global hotkey survives clone and serialization");
    Check(new AppSettings().PickerHotkeyKey==0xC0 && new AppSettings().PickerHotkeyModifiers==2,"legacy settings acquire Ctrl+tilde without Ctrl+Shift+F8");
    SafeState.Write(path,State(new[]{new ClipItem {Text="不能丟失",HistoryVisible=true}},pickerSettings));store.Reload();
    Check(store.PickerHotkey==(3u,86u),"Core uses persisted WPF hotkey source of truth");
    store.SetBubblePlacement(20,40);
    var currentSettings=JsonNode.Parse(File.ReadAllText(path))!["Settings"]!;
    Check(currentSettings["PickerWidth"]!.GetValue<double>()==456 && currentSettings["PickerHotkeyKey"]!.GetValue<int>()==86,"Core setting update preserves picker fields and history");
    pickerSettings.PickerHotkeyModifiers=6;pickerSettings.PickerHotkeyKey=119;
    SafeState.Write(path,State(Array.Empty<ClipItem>(),pickerSettings));store.Reload();
    Check(store.PickerHotkey==(2u,0xC0u),"Core rejects prohibited Ctrl+Shift+F8 setting and restores default");
    // 2.15.1: production folder projection, selection copy and dismissal policy.
    var scopes = PickerScope.Build(new[]{"工作", "工作/子層", "::favorite::/常用", "::temporary::/稍後", "::ai::/測試", "::quick::/問候", "工作"});
    Check(scopes[0].Kind == "History" && scopes.Count == 11, "folder selector defaults to History and preserves all canonical folder roots without duplicate paths");
    var organized = new ClipItem {Text="分類原文", HistoryVisible=false, Category="工作", Favorite=true, FavoriteFolder="常用", IsTemporary=true, TemporaryFolder="稍後", IsMarked=true, DraftText="未完成草稿", IsEditingText=true};
    var scoped = new QuickPickerModel {Scope=scopes.Single(s=>s.Path=="工作")}; scoped.Open(new[]{organized});
    Check(scoped.Search("").Single()==organized, "folder picker includes real organized content hidden from History");
    scoped.Scope=scopes[0];scoped.Open(new[]{organized});Check(scoped.Search("").Count==0,"History stays independent from classified source items");
    scoped.Scope=scopes.Single(s=>s.Path=="::favorite::/常用");scoped.Open(new[]{organized});Check(scoped.Search("").Single()==organized,"favorite subfolder uses existing FavoriteFolder ownership");
    scoped.Scope=scopes.Single(s=>s.Path=="::temporary::/稍後");scoped.Open(new[]{organized});Check(scoped.Search("").Single()==organized,"temporary subfolder uses existing TemporaryFolder ownership");
    organized.Category="AI 用語/測試";scoped.Scope=scopes.Single(s=>s.Path=="::ai::/測試");scoped.Open(new[]{organized});Check(scoped.Search("").Single()==organized,"AI folder matches canonical data category");
    organized.Category="快速用語/問候";scoped.Scope=scopes.Single(s=>s.Path=="::quick::/問候");scoped.Open(new[]{organized});Check(scoped.Search("").Single()==organized,"quick phrases folder matches canonical data category");
    organized.IsDeleted=true;scoped.Synchronize(new[]{organized});Check(scoped.Search("").Count==0 && organized.IsMarked && organized.DraftText=="未完成草稿","soft delete disappears from picker while manager draft and selection remain unchanged");
    var textBatch = new[]{new ClipItem {Text="中文一"},new ClipItem {Text="中文二"}};
    Check(PickerSelection.CombineText(textBatch)=="中文一"+Environment.NewLine+"中文二","multiple selected text items combine in caller display order");
    rejected=false;try{PickerSelection.CombineText(new[]{textBatch[0],new ClipItem {Kind="Image"}});}catch(InvalidOperationException){rejected=true;}Check(rejected,"mixed multiselection rejected before clipboard mutation");
    textBatch[1].IsDeleted=true;rejected=false;try{PickerSelection.CombineText(textBatch);}catch(InvalidOperationException){rejected=true;}Check(rejected,"stale multiselection cannot copy deleted content");
    Check(PickerDismissal.ShouldDismiss(true,false,false,false,true),"outside deactivation dismisses default picker");
    Check(!PickerDismissal.ShouldDismiss(true,false,false,false,false),"keep-open mode does not dismiss on outside focus change");
    Check(!PickerDismissal.ShouldDismiss(true,false,true,false,true) && !PickerDismissal.ShouldDismiss(true,false,false,true,true) && !PickerDismissal.ShouldDismiss(true,true,false,false,true),"paste transaction, owned preview/menu and active picker suppress dismissal");
    var dismissSettings=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings {PickerCloseOnDeactivate=false}.Clone()))!;
    Check(!dismissSettings.PickerCloseOnDeactivate && new AppSettings().PickerCloseOnDeactivate,"dismissal preference survives clone and persistence with safe legacy default");
    Check(new AppSettings().FloatingIconEnabled && !JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings {FloatingIconEnabled=false}.Clone()))!.FloatingIconEnabled, "floating icon visibility is an independent persistent setting with visible default");
    SafeState.Write(path,State(new[]{new ClipItem {Text="保護資料"}},new AppSettings {FloatingIconEnabled=false,PickerCloseOnDeactivate=false}));store.Reload();
    Check(!store.FloatingIconEnabled, "Core reads the same floating icon visibility preference as WPF");
    store.SetFloatingIconEnabled(true);currentSettings=JsonNode.Parse(File.ReadAllText(path))!["Settings"]!;
    Check(store.FloatingIconEnabled && !currentSettings["PickerCloseOnDeactivate"]!.GetValue<bool>() && JsonNode.Parse(File.ReadAllText(path))!["Items"]!.AsArray().Count==1, "Core floating visibility update preserves picker preference and user history");
    // Actual named-pipe boundary, using the production client/server (not a mock).
    var requests=new List<UiRequest>();
    try {
    using(var server=new UiServer(request=>{
        if(request.Command=="reject")throw new InvalidOperationException("rejected");
        requests.Add(request);return Task.CompletedTask;
    })) {
        await UiChannel.SendAsync(new UiRequest("quick",1234,77,99));
        Check(requests.Count==1 && requests[0].Target==1234 && requests[0].TargetPid==77 && requests[0].Started==99,"production IPC delivers exact original target and timestamp once");
        await UiChannel.SendAsync(new UiRequest("manage",5678,88));
        Check(requests.Select(x=>x.Command).SequenceEqual(new[]{"quick","manage"}),"IPC preserves sequential window switch command order");
        bool ipcRejected=false;try{await UiChannel.SendAsync(new UiRequest("reject"));}catch(IOException){ipcRejected=true;}
        Check(ipcRejected && requests.Count==2,"failed IPC command returns failure rather than success");
        await UiChannel.SendAsync(new UiRequest("bubble-placement",Left:-100,Top:60));
        Check(requests.Last().Left==-100 && requests.Last().Top==60,"bubble placement handed to sole UI state writer");
    }
    } catch (System.Net.Sockets.SocketException ex) when (!OperatingSystem.IsWindows()) {
        Console.WriteLine("UNVERIFIED: 4 production IPC checks blocked by host socket permission: "+ex.SocketErrorCode);
    }
    Console.WriteLine($"PASSED {checks} CHECKS; runtime/blocked checks reported separately above");
} finally { Directory.Delete(root,true); }
