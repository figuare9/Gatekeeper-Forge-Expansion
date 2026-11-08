using System.Text.RegularExpressions;
using System.Text.Json;
using BepInEx;
using Gatekeeper.Char_Scripts;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.Char_Scripts.General.Network;
using Gatekeeper.DownloadableContent;
using Gatekeeper.EnvironmentStuff.Obelisks;
using Gatekeeper.General.Currency;
using Gatekeeper.General.InputSystem;
using Gatekeeper.General.Predictor;
using Gatekeeper.General.SaveLoad;
using Gatekeeper.Infrastructure.Providers.InfoProviders;
using Gatekeeper.Items;
using Gatekeeper.MainMenuScripts.Database.ItemsDatabaseController;
using Gatekeeper.Utility.SceneHelper;
using Gatekeeper.Mods;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static partial class ShopUI
{
    internal static bool IsOpen { get; private set; }
    internal static bool InRestArea => Plugin.ShopActive && Plugin.RestAreaGameplay;
    private static bool Preview => Plugin.Current?.PreviewAtMenu.Value == true && GlobalPredictor.GameState == GameState.Menu;
    private static readonly List<Entry> catalog = new();
    private static List<Entry> filtered = new();
    private static Entry? selected;
    private static string query = "", lastQuery = "", message = "Choose an unlocked item to see its details.";
    private static int filter = -1, lastFilter = -2;
    private static float scrollOffset;
    private static bool draggingScroll;
    private static float dragOffset;
    private static bool searchFocused;
    private static bool selling;
    private static CharManager? lockedCharacter;
    private static bool previousInputLock, previousCursor;
    private static CursorLockMode previousCursorLock;
    private static float nextRefresh;
    private static int openFrames;
    private static int renderedFrames;
    private static bool previewCaptured, previewOpened;
    private static GUIStyle? titleStyle, textStyle, smallStyle, buttonStyle, searchStyle, headingStyle;
    private static string? lastError;
    private static readonly HashSet<ItemID> iconErrors = new();
    private static Internal_DrawTextureArguments? iconDraw;
    private static readonly Dictionary<string,Color> colors=new();
    private static readonly Dictionary<string,Rect> rects=new();
    private static Color Backdrop => colors["panel"];
    private static Color Surface => colors["row"];
    private static Color Accent => colors["gold"];
    private static Color Muted => colors["muted"];
    private sealed record Entry(ItemDatabaseInfo Info, string Name, string Description);
    internal static void Open()
    {
        if (IsOpen || !InRestArea && !Preview) return;
        if (!Preview && (PlayableCharactersHolder.MyCharacter == null || !WorldShop.CanInteract(PlayableCharactersHolder.MyCharacter))) return;
        if (DatabaseInfoProvider.Items == null) return;
        RefreshCatalog();
        IsOpen = true; openFrames = 0; OpenController();
        previousCursor = Cursor.visible; previousCursorLock = Cursor.lockState;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        var player = PlayableCharactersHolder.MyCharacter;
        if (InRestArea && player != null)
        {
            lockedCharacter = player; previousInputLock = player.Controller.IsInputLocked;
            player.OwnerSetAttack(false);
            player.Controller.SetMovementDirection(Vector3.zero);
            player.Controller.SetInputLockState(true);
        }
    }

    internal static void Close(bool force = false)
    {
        if (!IsOpen || !ExchangeClient.CanClose && !force) return;
        IsOpen = false; searchFocused = false; draggingScroll = false; CloseController();
        try
        {
            if (lockedCharacter != null && lockedCharacter.Controller != null) lockedCharacter.Controller.SetInputLockState(previousInputLock);
            Cursor.visible = previousCursor; Cursor.lockState = previousCursorLock;
        }
        catch (Exception ex) { Plugin.Current?.Log.LogWarning("Shop input restore: " + ex.Message); }
        lockedCharacter = null;
    }

    internal static void Tick()
    {
        if (IsOpen && !InRestArea && !Preview) Close(force: true);
        if (Preview && !previewOpened && DatabaseInfoProvider.Items != null)
        { previewOpened = true; Open(); }
        if (!IsOpen) return;
        ControllerTick();
        if (!IsOpen) return;
        Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
        if (Time.unscaledTime > nextRefresh) { nextRefresh = Time.unscaledTime + 2; RefreshCatalog(); }
        if (Preview && !previewCaptured && ++openFrames > 90 && renderedFrames > 30)
        {
            previewCaptured = true;
            ScreenCapture.CaptureScreenshot(Path.Combine(Paths.ConfigPath, Plugin.Id + ".shop-preview.png"));
        }
    }

    private static bool DlcAvailable(ItemDatabaseInfo item) => !item.IsDlcElement ||
        DlcController.Instance != null && DlcController.Instance.IsAvailable(item.dlcId, true);

    private static bool Unlocked(ItemDatabaseInfo item)
    {
        if(CannonRegistry.Is(item))return CannonRegistry.Enabled && CannonProgress.Unlocked;
        // ItemDatabaseInfo.unlocked is the asset's default. The native item pool consults
        // saved progression by BaseDatabaseInfo.Id, so the shop uses that same authority.
        try { return SaveLoadManager.GetUnlockedStatus(item.Id); } catch { return false; }
    }

    private static void RefreshCatalog()
    {
        var provider = DatabaseInfoProvider.Items;
        if (provider == null) return;
        var previousId = selected?.Info.ItemId;
        catalog.Clear();
        var seen = new HashSet<ItemID>();
        void Add(ItemDatabaseInfo item)
        {
            if (item == null || !ExchangeRules.AllowedType((int)item.itemType) || !seen.Add(item.ItemId)) return;
            var owner=PlayableCharactersHolder.MyCharacter;
            if (selling ? owner==null || owner.ItemManager.GetItemQuantity(item.ItemId)<=0 : !Unlocked(item) || !DlcAvailable(item)) return;
            var name = item.itemNameKey.ToString();
            if (string.IsNullOrWhiteSpace(name)) name = item.ItemId.ToString();
            var lore = item.itemLongDescKey.ToString();
            var description = item.itemStatsKey.ToString();
            try
            {
                var localizer = new ItemLocalizer();
                var player = PlayableCharactersHolder.MyCharacter;
                if (player != null)
                {
                    localizer.Setup(item, player.Cast<Gatekeeper.Char_Scripts.Base.IPlayerCharacter>());
                    var key = item.itemStatsKey;
                    description = localizer.Translate(ref key, true);
                }
            }
            catch { /* Plain localized description is available before a character is spawned. */ }
            var stats = string.Join("\n", Rich(description).Split('\n').Where(line => !line.Contains("{["))).Trim();
            catalog.Add(new(item, Clean(name), stats + (string.IsNullOrWhiteSpace(lore) ? "" : "\n\n" + Clean(lore))));
        }
        foreach (var pair in provider.ItemInfos) Add(pair.Value);
        catalog.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
        Filter();
        selected = filtered.FirstOrDefault(x => x.Info.ItemId == previousId) ?? filtered.FirstOrDefault();
    }

    private static string Clean(string value) => Regex.Replace(value ?? "", "<[^>]+>", "");
    private static string Rich(string value) => Regex.Replace(value ?? "", @"<(?!/?(?:color(?:=#[a-fA-F0-9]{6,8})?|b|i)>)[^>]+>", "");
    internal static void RefreshInventory() { RefreshCatalog(); nextRefresh=Time.unscaledTime+2; }

    private static void Filter()
    {
        filtered = catalog.Where(x => (filter < 0 || (int)x.Info.itemType == filter) &&
            (string.IsNullOrWhiteSpace(query) || x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             x.Info.ItemId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        selected = filtered.FirstOrDefault(x => x.Info.ItemId == selected?.Info.ItemId) ?? filtered.FirstOrDefault();
        lastQuery = query; lastFilter = filter;
    }

    private static int Price(ItemDatabaseInfo info) => Preview
        ? selling ? ExchangeRules.SalePrice(Math.Max(0,info.itemPedestalCost)) : ExchangeRules.Markup(Math.Max(0, info.itemPedestalCost))
        : selling ? ExchangeClient.Quote(info)?.Sale ?? -1 : ExchangeClient.Quote(info)?.Price ?? -1;

    private static void Buy(Entry entry) => ExchangeClient.Buy(entry.Info);
    internal static bool LocallyEligible(ItemDatabaseInfo info) => ExchangeRules.AllowedType((int)info.itemType) && Unlocked(info) && DlcAvailable(info);
    internal static void SetMessage(string text) => message = text;

    private static string CurrencyName(CurrencyType currency) => currency switch
    { CurrencyType.Prism => "Prisms", CurrencyType.Omni => "Omni", CurrencyType.Scrap => "Scrap", CurrencyType.MonolithKey => "Monolith keys", _ => currency.ToString() };

    internal static void ReportError(Exception ex)
    {
        if (lastError == ex.Message) return;
        lastError = ex.Message; Plugin.Current?.Log.LogError("Quiet Bay shop: " + ex);
        message = "Shop unavailable: " + ex.Message;
        Close(force: true);
    }

    private static void Styles()
    {
        if (titleStyle != null) return;
        using var source=typeof(ShopUI).Assembly.GetManifestResourceStream("Gatekeeper.CustomMods.Assets.exchange-ui.json")
            ?? throw new InvalidOperationException("Exchange interface layout is missing.");
        using var skin=JsonDocument.Parse(source);
        foreach (var property in skin.RootElement.GetProperty("colors").EnumerateObject())
        { var c=property.Value; colors[property.Name]=new(c[0].GetSingle()/255,c[1].GetSingle()/255,c[2].GetSingle()/255,c[3].GetSingle()/255); }
        foreach (var property in skin.RootElement.GetProperty("rects").EnumerateObject())
        { var p=property.Value; rects[property.Name]=new(p[0].GetSingle(),p[1].GetSingle(),p[2].GetSingle(),p[3].GetSingle()); }
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 25, fontStyle = FontStyle.Bold };
        headingStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = true };
        textStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText=true };
        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        searchStyle = new GUIStyle(GUI.skin.textField) { fontSize = 17 };
    }

    private static void Fill(Rect rect, Color color)
    {
        var old = GUI.color; GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old;
    }

    private static void Text(Rect rect, string text, GUIStyle? style = null, Color? color = null)
    {
        var old = GUI.contentColor; GUI.contentColor = color ?? Color.white;
        GUI.Label(rect, text, style ?? textStyle!); GUI.contentColor = old;
    }

    private static void Border(Rect r,Color c,float thickness=1)
    {
        Fill(new(r.x,r.y,r.width,thickness),c); Fill(new(r.x,r.yMax-thickness,r.width,thickness),c);
        Fill(new(r.x,r.y,thickness,r.height),c); Fill(new(r.xMax-thickness,r.y,thickness,r.height),c);
    }
    private static bool FlatButton(Rect r,string label,bool selected=false)
    {
        Fill(r,selected ? new Color(.24f,.20f,.14f,.95f) : Surface);
        Border(r,selected ? Accent : colors["border"]);
        var enabled=GUI.enabled;
        Text(new(r.x+9,r.y+8,r.width-18,r.height-8),label,headingStyle,enabled ? selected ? Accent : colors["text"] : Muted);
        return GUI.Button(r,"",GUIStyle.none);
    }
    private static void Panel(Rect r,string caption,string count="")
    {
        Fill(r,Backdrop); Border(r,colors["border"],2);
        Fill(new(r.x+3,r.y+3,r.width-6,32),new Color(.05f,.09f,.13f,.95f));
        Text(new(r.x+10,r.y+7,r.width-85,24),caption,headingStyle,Accent);
        Text(new(r.xMax-72,r.y+8,64,24),count,smallStyle,colors["text"]);
    }
    private static void Mode(bool sell)
    {
        if (selling==sell) return;
        selling=sell; scrollOffset=0; query=""; searchFocused=false; RefreshInventory();
        message=sell ? "Sell one stack at a time for 50% of its current item value." : "Choose an unlocked item. Purchase prices include a 26.5% premium.";
    }
    internal static void Draw()
    {
        if (!InRestArea && !Preview) return;
        Styles();
        var oldMatrix=GUI.matrix; var oldColor=GUI.color; var oldContent=GUI.contentColor; var oldBackground=GUI.backgroundColor; var oldDepth=GUI.depth; var oldEnabled=GUI.enabled;
        GUI.depth=-10000;
        try
        {
            var scale=Mathf.Min(Screen.width/1280f,Screen.height/800f);
            if (scale<=0 || !IsOpen) return;
            GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-1280f*scale)/2,(Screen.height-800f*scale)/2,0),Quaternion.identity,new Vector3(scale,scale,1));
            Fill(new(-Screen.width/scale,-Screen.height/scale,Screen.width/scale*3,Screen.height/scale*3),colors["backdrop"]);
            Text(new(28,24,730,40),"ITEM EXCHANGE",titleStyle,Accent);
            var player=PlayableCharactersHolder.MyCharacter;
            var balance=player!=null ? ShopWallet.For(player,CurrencyType.Prism)?.Read().Displayed ?? 0 : 0;
            Text(rects["balance"],$"YOUR PRISMS   {balance}",headingStyle,colors["text"]);
            var input=Event.current;
            GUI.enabled=ExchangeClient.CanClose;
            var close=rects["close"];
            Text(close,GameplayInputManager.CurrentDeviceIsJoystick?"BACK\nCLOSE":"[ESC]\nCLOSE",headingStyle,colors["orange"]);
            if (GUI.Button(close,"",GUIStyle.none) || input.type==EventType.KeyDown && input.keyCode==KeyCode.Escape && ExchangeClient.CanClose)
            { Close(); if (input.type==EventType.KeyDown) input.Use(); return; }
            GUI.enabled=!ExchangeClient.Busy;
            if (FlatButton(rects["buy"],"BUY",!selling)) Mode(false);
            if (FlatButton(rects["sell"],"SELL",selling)) Mode(true);
            GUI.enabled=true;
            var search=rects["search"];
            Fill(search,Surface); Border(search,searchFocused ? Accent : colors["border"]);
            if (GUI.Button(search,"",GUIStyle.none)) searchFocused=true;
            Text(new(search.x+12,search.y+9,search.width-24,28),query.Length>0 ? query+(searchFocused ? " |":"") : "SEARCH ITEMS...",textStyle,query.Length>0 ? colors["text"] : Muted);
            if (input.type==EventType.MouseDown && !search.Contains(input.mousePosition)) searchFocused=false;
            if (searchFocused && input.type==EventType.KeyDown)
            {
                if (input.keyCode==KeyCode.Backspace && query.Length>0) query=query[..^1];
                else if (input.character>=' ' && query.Length<80) query+=input.character;
                else if (input.keyCode==KeyCode.Return) searchFocused=false;
                input.Use();
            }
            if (query!=lastQuery || filter!=lastFilter) { Filter(); scrollOffset=0; }
            var groups=rects["groups"]; var items=rects["items"]; var details=rects["details"];
            Panel(groups,"ITEM GROUPS"); Panel(items,selling ? "CURRENT INVENTORY":"AVAILABLE ITEMS",filtered.Count.ToString()); Panel(details,"ITEM DETAILS");
            var names=new[] {"ALL ITEMS","MODIFIERS","STRUCTURE CHANGE","AMULETS","RUNES OF CREATION","CURSED SIGNATURES"};
            for (var i=0;i<names.Length;i++)
            {
                var category=i-1;
                var group=new Rect(groups.x+9,groups.y+46+i*79,groups.width-18,73);
                var entries=catalog.Where(e=>category<0 || (int)e.Info.itemType==category).ToArray();
                Fill(group,filter==category ? new Color(.20f,.17f,.12f,.95f) : new Color(.025f,.045f,.07f,.9f));
                Border(group,filter==category ? Accent : colors["border"]);
                Text(new(group.x+7,group.y+5,group.width-12,22),names[i],smallStyle,filter==category ? Accent : colors["text"]);
                for (var n=0;n<Math.Min(4,entries.Length);n++) Icon(entries[n].Info,new(group.x+8+n*38,group.y+30,32,32));
                Text(new(group.xMax-36,group.y+35,32,25),entries.Length.ToString(),smallStyle,Muted);
                if (GUI.Button(group,"",GUIStyle.none)) { filter=category; scrollOffset=0; searchFocused=false; Filter(); }
            }
            Text(new(groups.x+14,groups.yMax-47,groups.width-28,38),"CURRENT RUN ONLY\nTRIADS ARE NOT TRADED",smallStyle,Muted);
            DrawItems(new(items.x+9,items.y+46,items.width-18,items.height-59));
            if (selected!=null) Detail(selected,details.x+18,details.y+49);
            else Text(new(details.x+18,details.y+65,details.width-36,100),selling ? "Your sellable inventory is empty." : "Choose an item to see its details.",textStyle,Muted);
            Text(new(28,749,1155,40),Preview ? "OFFLINE PREVIEW — TRADING DISABLED" : ControllerHint+message,smallStyle,Muted);
            if (input.type==EventType.Repaint) renderedFrames++;
        }
        finally { GUI.matrix=oldMatrix; GUI.color=oldColor; GUI.contentColor=oldContent; GUI.backgroundColor=oldBackground; GUI.depth=oldDepth; GUI.enabled=oldEnabled; }
    }

    private static string Rarity(ItemType type) => type switch
    { ItemType.Modifiers => "Modifier", ItemType.StructureChangers => "Structure changer", ItemType.Amulets => "Amulet", ItemType.RunesOfCreation => "Rune of creation", ItemType.CursedSignatures => "Cursed signature", _ => type.ToString() };

    private static void Detail(Entry entry,float x,float y)
    {
        const float width=349;
        var info=entry.Info; var player=PlayableCharactersHolder.MyCharacter;
        Icon(info,new(x,y,70,70));
        Fill(new(x+79,y,width-79,32),colors["namebar"]);
        Text(new(x+86,y+4,width-93,63),entry.Name.ToUpperInvariant(),headingStyle,colors["text"]);
        Text(new(x+84,y+70,width-84,23),Rarity(info.itemType).ToUpperInvariant(),smallStyle,Muted);
        Fill(new(x,y+102,width,1),colors["border"]);
        Text(new(x,y+119,width,244),entry.Description,textStyle,colors["text"]);
        var count=player!=null ? player.ItemManager.GetItemQuantity(info.ItemId):0;
        Text(new(x,y+371,width,27),$"OWNED THIS RUN   {count}",smallStyle,Muted);
        var price=Price(info); var funds=player!=null ? ShopWallet.For(player,CurrencyType.Prism)?.Read() : null;
        Text(new(x,y+400,width,32),price<0 ? "CHECKING VALUE..." : $"{(selling ? "+":"")}{price} PRISMS",titleStyle,Accent);
        var quote=!Preview ? ExchangeClient.Quote(info):null;
        Text(new(x,y+440,width,45),quote?.Native>=0 ? $"BASE ITEM VALUE   {quote.Native}\n"+(selling ? "SELL VALUE   50% (ROUNDED DOWN)":"PURCHASE PREMIUM   +26.5%") : "Waiting for the current item price.",smallStyle,Muted);
        GUI.enabled=CanTrade(info);
        var label=ExchangeClient.Busy ? "CONFIRMING TRADE..." : selling ? "SELL ONE" : info.IsMaxStacksOrHigher(count) ? "STACK LIMIT REACHED" : "BUY ONE";
        if (FlatButton(new(x,y+494,width,47),label,true)) { if (selling) ExchangeSales.Sell(info); else Buy(entry); }
        GUI.enabled=true;
    }

    private static void Icon(ItemDatabaseInfo info, Rect rect)
    {
        Fill(rect, new Color(.20f, .28f, .30f, 1));
        if (Event.current.type != EventType.Repaint) return;
        try
        {
            var sprite = info.DatabaseIcon; if (sprite == null || sprite.texture == null) return;
            var texture = sprite.texture; var area = sprite.textureRect;
            // The unstripped GUI helper uses initobj on a native boxed value-type
            // wrapper. Its null native pointer causes ObjectCollectedException.
            // Allocate the wrapper normally and invoke the existing native draw.
            iconDraw ??= new Internal_DrawTextureArguments();
            iconDraw.texture = texture; iconDraw.mat = GUI.blendMaterial;
            iconDraw.screenRect = rect;
            iconDraw.sourceRect = new Rect(area.x / texture.width, area.y / texture.height, area.width / texture.width, area.height / texture.height);
            iconDraw.color = Color.white;
            iconDraw.leftBorderColor = iconDraw.rightBorderColor = iconDraw.topBorderColor = iconDraw.bottomBorderColor = Color.white;
            var args = iconDraw;
            Graphics.Internal_DrawTexture(ref args);
        }
        catch (Exception ex)
        {
            if (iconErrors.Add(info.ItemId)) Plugin.Logger.LogWarning($"Shop icon {info.ItemId}: {ex}");
        }
    }

    private static void DrawItems(Rect viewport)
    {
        const float rowHeight=71;
        var contentHeight=filtered.Count*rowHeight;
        var max=Math.Max(0,contentHeight-viewport.height);
        var input=Event.current;
        if (input.type==EventType.ScrollWheel && viewport.Contains(input.mousePosition)) { scrollOffset+=input.delta.y*32; input.Use(); }
        if (!searchFocused && input.type==EventType.KeyDown)
        {
            if (input.keyCode==KeyCode.Home) scrollOffset=0;
            else if (input.keyCode==KeyCode.End) scrollOffset=max;
            else if (input.keyCode==KeyCode.PageDown) scrollOffset+=viewport.height-rowHeight;
            else if (input.keyCode==KeyCode.PageUp) scrollOffset-=viewport.height-rowHeight;
        }
        scrollOffset=Math.Clamp(scrollOffset,0,max);
        var track=new Rect(viewport.x,viewport.y,8,viewport.height);
        var thumbHeight=Math.Min(viewport.height,Math.Max(32,viewport.height*viewport.height/Math.Max(1,contentHeight)));
        var travel=viewport.height-thumbHeight;
        var thumb=new Rect(track.x,track.y+(max>0 ? scrollOffset/max*travel:0),track.width,thumbHeight);
        if (max>0)
        {
            if (input.type==EventType.MouseDown && input.button==0 && track.Contains(input.mousePosition))
            { draggingScroll=true; searchFocused=false; dragOffset=thumb.Contains(input.mousePosition) ? input.mousePosition.y-thumb.y:thumbHeight/2; scrollOffset=Math.Clamp((input.mousePosition.y-track.y-dragOffset)/travel*max,0,max); input.Use(); }
            else if (draggingScroll && input.type==EventType.MouseDrag) { scrollOffset=Math.Clamp((input.mousePosition.y-track.y-dragOffset)/travel*max,0,max); input.Use(); }
            else if (draggingScroll && input.type==EventType.MouseUp) { draggingScroll=false; input.Use(); }
            Fill(track,Surface); Fill(thumb,Accent);
        }
        var rowWidth=viewport.width-20;
        GUI.BeginGroup(new(viewport.x+20,viewport.y,rowWidth,viewport.height));
        try
        {
            if (filtered.Count==0) Text(new(12,18,rowWidth-24,80),selling ? "No owned items match this group or search." : "No unlocked items match this group or search.",textStyle,Muted);
            var first=Math.Max(0,(int)(scrollOffset/rowHeight));
            var end=Math.Min(filtered.Count,(int)Math.Ceiling((scrollOffset+viewport.height)/rowHeight));
            for (var i=first;i<end;i++)
            {
                var entry=filtered[i]; var rowY=i*rowHeight-scrollOffset;
                var row=new Rect(0,rowY,rowWidth,rowHeight-6);
                Fill(row,Surface);
                Fill(new(66,rowY+2,rowWidth-68,29),colors["namebar"]);
                if (selected?.Info.ItemId==entry.Info.ItemId) Border(row,Accent,2);
                Icon(entry.Info,new(4,rowY+4,57,57));
                Text(new(73,rowY+4,rowWidth-80,27),entry.Name.ToUpperInvariant(),headingStyle,colors["text"]);
                var player=PlayableCharactersHolder.MyCharacter;
                var owned=player!=null ? player.ItemManager.GetItemQuantity(entry.Info.ItemId):0;
                Text(new(73,rowY+37,rowWidth-235,22),$"OWNED  {owned}",smallStyle,Muted);
                var price=Price(entry.Info);
                Text(new(rowWidth-158,rowY+36,151,25),price<0 ? "CHECKING..." : $"{(selling ? "+":"")}{price} PRISMS",smallStyle,Accent);
                if (GUI.Button(row,"",GUIStyle.none)) { selected=entry; searchFocused=false; }
            }
        }
        finally { GUI.EndGroup(); }
    }
}
