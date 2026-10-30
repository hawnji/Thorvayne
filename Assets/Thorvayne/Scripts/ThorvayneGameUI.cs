using System.Collections.Generic;
using UnityEngine;

// On-screen game UI: message box, action prompt, XP drops, level-ups,
// inventory (I or Tab), equipment (G) and skills (K) panels with item icons and hover tooltips.
// The mouse is freed while a panel is open. In the inventory, click gear to wear or wield it, click anything
// else (or right-click) to drop it, Shift-click to drop all of that kind. In the equipment panel, click an item to take it off.
public class ThorvayneGameUI : MonoBehaviour
{
    public KeyCode inventoryKey = KeyCode.I;
    public KeyCode inventoryKey2 = KeyCode.Tab;
    public KeyCode skillsKey = KeyCode.K;
    public KeyCode equipmentKey = KeyCode.G;

    ThorvaynePlayer player;
    ThorvayneSkills skills;
    ThorvayneInventory inventory;
    ThorvayneWoodcutting woodcutting;
    ThorvayneEquipment equipment;
    bool showInventory, showSkills, showEquipment;

    readonly List<string> messages = new List<string>();
    class Drop { public string text; public float age; }
    readonly List<Drop> drops = new List<Drop>();
    string levelUpText = "";
    float levelUpTimer;
    GUIStyle label, small, centre, big;

    void Start()
    {
        player = GetComponent<ThorvaynePlayer>();
        skills = GetComponent<ThorvayneSkills>();
        inventory = GetComponent<ThorvayneInventory>();
        woodcutting = GetComponent<ThorvayneWoodcutting>();
        equipment = GetComponent<ThorvayneEquipment>();
        if (skills != null) skills.XpGained += OnXp;
        Message("Welcome to Thorvayne. Walk up to a tree and press E to chop it.");
    }

    void OnDestroy() { if (skills != null) skills.XpGained -= OnXp; }

    public void Message(string text)
    {
        messages.Add(text);
        if (messages.Count > 6) messages.RemoveAt(0);
    }

    void OnXp(string skill, float xp, int newLevel)
    {
        drops.Add(new Drop { text = $"+{xp:0.#} {skill} XP" });
        if (newLevel > 0)
        {
            levelUpText = $"Congratulations, you've just advanced a {skill} level!\nYou are now level {newLevel}.";
            levelUpTimer = 4f;
            Message($"Congratulations, you've just advanced a {skill} level. You are now level {newLevel}.");
        }
    }

    void Update()
    {
        if (ThorvayneKeys.Down(inventoryKey) || ThorvayneKeys.Down(inventoryKey2)) showInventory = !showInventory;
        if (ThorvayneKeys.Down(skillsKey)) showSkills = !showSkills;
        if (ThorvayneKeys.Down(equipmentKey)) showEquipment = !showEquipment;
        if (equipment == null) equipment = GetComponent<ThorvayneEquipment>();
        bool esc = ThorvayneKeys.Down(KeyCode.Escape);
        if (esc) showInventory = showSkills = showEquipment = false;
        if (player != null) player.SetMenuOpen(showInventory || showSkills || showEquipment, !esc);   // Esc leaves the mouse free

        for (int i = drops.Count - 1; i >= 0; i--)
        {
            drops[i].age += Time.deltaTime;
            if (drops[i].age > 2.5f) drops.RemoveAt(i);
        }
        if (levelUpTimer > 0) levelUpTimer -= Time.deltaTime;
    }

    // ---------------- look ----------------
    // Dark wood-and-brass panels drawn from small generated textures (no art files needed), item icons from
    // Resources/Icons/<item id>.png (Extras/make_icons.py), and a UI scale that grows with the screen height.
    static readonly Color Gold = new Color(1f, 0.82f, 0.42f), Faint = new Color(1f, 0.92f, 0.8f, 0.35f), Bad = new Color(1f, 0.45f, 0.35f);
    GUIStyle panel, header, slot, slotHover, tip, bar, barFill;
    readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
    string tipTitle, tipBody;
    public float uiScale = 0f;   // 0 = automatic (1 at 1080p, bigger on taller screens)

    static Texture2D Frame(Color fill, Color fill2, Color edge, Color inner)
    {
        const int n = 16;
        var t = new Texture2D(n, n) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(n - 1 - x, n - 1 - y));
                t.SetPixel(x, y, d == 0 ? edge : d == 1 ? inner : Color.Lerp(fill, fill2, y / (n - 1f)));
            }
        t.Apply();
        return t;
    }

    static GUIStyle Boxed(Texture2D t, int border)
    {
        var st = new GUIStyle();
        st.normal.background = t;
        st.border = new RectOffset(border, border, border, border);
        return st;
    }

    void Styles()
    {
        if (label != null) return;
        label = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
        label.normal.textColor = new Color(0.96f, 0.92f, 0.84f);
        small = new GUIStyle(label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        centre = new GUIStyle(label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        big = new GUIStyle(centre) { fontSize = 22 };
        big.normal.textColor = Gold;
        title = new GUIStyle(label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        title.normal.textColor = Gold;
        count = new GUIStyle(label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft, wordWrap = false };
        count.normal.textColor = new Color(1f, 0.95f, 0.3f);

        panel = Boxed(Frame(new Color(0.13f, 0.1f, 0.075f, 0.95f), new Color(0.2f, 0.155f, 0.11f, 0.95f), new Color(0.04f, 0.03f, 0.02f), new Color(0.58f, 0.44f, 0.22f)), 3);
        header = Boxed(Frame(new Color(0.24f, 0.17f, 0.1f), new Color(0.36f, 0.26f, 0.14f), new Color(0.04f, 0.03f, 0.02f), new Color(0.7f, 0.54f, 0.28f)), 3);
        slot = Boxed(Frame(new Color(0.07f, 0.055f, 0.045f, 0.9f), new Color(0.1f, 0.08f, 0.06f, 0.9f), new Color(0.03f, 0.02f, 0.015f), new Color(0.3f, 0.23f, 0.15f)), 3);
        slotHover = Boxed(Frame(new Color(0.16f, 0.13f, 0.08f, 0.95f), new Color(0.24f, 0.19f, 0.11f, 0.95f), new Color(0.03f, 0.02f, 0.015f), new Color(0.95f, 0.75f, 0.35f)), 3);
        tip = Boxed(Frame(new Color(0.06f, 0.05f, 0.04f, 0.96f), new Color(0.1f, 0.08f, 0.06f, 0.96f), new Color(0.02f, 0.015f, 0.01f), new Color(0.75f, 0.58f, 0.3f)), 3);
        bar = Boxed(Frame(new Color(0.05f, 0.04f, 0.03f), new Color(0.08f, 0.06f, 0.05f), new Color(0.02f, 0.015f, 0.01f), new Color(0.2f, 0.16f, 0.1f)), 2);
        barFill = Boxed(Frame(new Color(0.2f, 0.5f, 0.2f), new Color(0.38f, 0.78f, 0.32f), new Color(0.1f, 0.25f, 0.1f), new Color(0.5f, 0.9f, 0.45f)), 2);
        foreach (var st in new[] { panel, header, slot, slotHover, tip })
        {
            st.padding = new RectOffset(8, 8, 6, 6);
            st.normal.textColor = label.normal.textColor;
        }
    }
    GUIStyle title, count;

    Texture2D Icon(string id)
    {
        if (!icons.TryGetValue(id, out var tex)) icons[id] = tex = Resources.Load<Texture2D>("Icons/" + id);
        return tex;
    }

    Rect Panel(Rect r, string heading)
    {
        GUI.Box(r, GUIContent.none, panel);
        var hr = new Rect(r.x + 4, r.y + 4, r.width - 8, 26);
        GUI.Box(hr, GUIContent.none, header);
        Shadowed(hr, heading, title);
        return new Rect(r.x + 10, r.y + 38, r.width - 20, r.height - 48);
    }

    // one item slot: frame, icon (or a coloured block if there's no icon), stack count; true when the mouse is on it
    bool ItemSlot(Rect cell, string id, int n)
    {
        bool over = cell.Contains(Event.current.mousePosition);
        GUI.Box(cell, GUIContent.none, over ? slotHover : slot);
        if (id == null) return over;
        var icon = Icon(id);
        var inner = new Rect(cell.x + 3, cell.y + 3, cell.width - 6, cell.height - 6);
        if (icon != null) GUI.DrawTexture(inner, icon, ScaleMode.ScaleToFit);
        else
        {
            var item = ThorvayneItems.Get(id);
            var c = GUI.color; GUI.color = item != null ? item.color : Color.magenta;
            GUI.DrawTexture(new Rect(cell.x + 10, cell.y + 10, cell.width - 20, cell.height - 20), Texture2D.whiteTexture);
            GUI.color = c;
        }
        if (n > 1) Shadowed(new Rect(cell.x + 4, cell.y + 2, cell.width, 16), n.ToString(), count);
        return over;
    }

    void Tip(string heading, string body) { tipTitle = heading; tipBody = body; }

    string ItemInfo(ThorvayneItems.Item item, string actions)
    {
        var lines = new List<string>();
        if (item.skill != null)
        {
            bool ok = skills == null || skills.Level(item.skill) >= item.level;
            string need = $"Requires {item.skill} {item.level}";
            lines.Add(ok ? need : $"<color=#ff7a60>{need}</color>");
        }
        if (ThorvayneItems.IsAxe(item)) lines.Add($"Chopping power {item.power}");
        if (item.slot != null) lines.Add($"Slot: {item.slot}");
        lines.Add($"<color=#c9b48a>{actions}</color>");
        return string.Join("\n", lines);
    }

    void DrawTip()
    {
        if (tipTitle == null) return;
        var m = Event.current.mousePosition;
        var body = new GUIContent(tipBody ?? "");
        tipStyle.richText = true;
        float w = Mathf.Max(170, Mathf.Max(title.CalcSize(new GUIContent(tipTitle)).x, tipStyle.CalcSize(body).x) + 20);
        float h = 32 + (string.IsNullOrEmpty(tipBody) ? 0 : tipStyle.CalcHeight(body, w - 20));
        var r = new Rect(m.x + 16, m.y + 12, w, h);
        if (r.xMax > uiW - 4) r.x = m.x - w - 8;
        if (r.yMax > uiH - 4) r.y = uiH - h - 4;
        GUI.Box(r, GUIContent.none, tip);
        var tt = new GUIStyle(title) { alignment = TextAnchor.UpperLeft };
        GUI.Label(new Rect(r.x + 10, r.y + 6, w - 20, 22), tipTitle, tt);
        if (!string.IsNullOrEmpty(tipBody)) GUI.Label(new Rect(r.x + 10, r.y + 28, w - 20, h - 30), body, tipStyle);
    }
    GUIStyle tipStyle => _tipStyle ?? (_tipStyle = new GUIStyle(label) { fontSize = 12, richText = true });
    GUIStyle _tipStyle;
    float uiW, uiH;

    void OnGUI()
    {
        Styles();
        float scale = uiScale > 0 ? uiScale : Mathf.Max(1f, Screen.height / 1080f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float w = uiW = Screen.width / scale, h = uiH = Screen.height / scale;
        tipTitle = tipBody = null;

        // message box (bottom left)
        var mb = new Rect(10, h - 156, 480, 146);
        GUI.Box(mb, GUIContent.none, panel);
        for (int i = 0; i < messages.Count; i++)
            GUI.Label(new Rect(mb.x + 12, mb.y + 8 + i * 22, mb.width - 24, 22), messages[i], label);

        // action prompt (above the message box, centred)
        if (woodcutting != null && woodcutting.Prompt != "")
            Shadowed(new Rect(0, h * 0.72f, w, 30), woodcutting.Prompt, centre);

        // XP drops (rise and fade on the right)
        for (int i = 0; i < drops.Count; i++)
        {
            var d = drops[i];
            var c = GUI.color; GUI.color = new Color(1, 1, 1, Mathf.Clamp01(2.5f - d.age));
            Shadowed(new Rect(w - 330, h * 0.45f - d.age * 40f - i * 26, 300, 26), d.text, centre);
            GUI.color = c;
        }

        if (levelUpTimer > 0) Shadowed(new Rect(0, h * 0.3f, w, 60), levelUpText, big);

        // panels side by side from the right: inventory, then equipment, then skills
        float x = w - 10;
        if (showInventory) x = InventoryPanel(x, h) - 10;
        if (showEquipment && equipment != null) x = EquipmentPanel(x, h) - 10;
        if (showSkills) SkillsPanel(x, h);
        DrawTip();
    }

    const int Cell = 50, Gap = 6;

    float InventoryPanel(float right, float h)
    {
        const int cols = 4;
        int rows = ThorvayneInventory.Size / cols;
        float pw = cols * (Cell + Gap) - Gap + 20, ph = rows * (Cell + Gap) - Gap + 74;
        var r = new Rect(right - pw, h - ph - 10, pw, ph);
        var inner = Panel(r, "Inventory");
        for (int i = 0; i < ThorvayneInventory.Size; i++)
        {
            var cell = new Rect(inner.x + (i % cols) * (Cell + Gap), inner.y + (i / cols) * (Cell + Gap), Cell, Cell);
            var slotData = inventory.slots[i];
            bool over = ItemSlot(cell, slotData?.id, slotData != null ? slotData.count : 0);
            if (slotData == null) continue;
            var item = ThorvayneItems.Get(slotData.id);
            string name = item != null ? item.name : slotData.id;
            bool gear = item != null && item.slot != null && equipment != null;
            string verb = item != null && item.slot == "Weapon" ? "Wield" : "Wear";
            if (over && item != null)
                Tip(slotData.count > 1 ? $"{name} x{slotData.count}" : name,
                    ItemInfo(item, gear ? $"Click: {verb}   Right-click: Drop" : "Click: Drop   Shift: Drop all"));
            var e = Event.current;
            if (e.type == EventType.MouseDown && cell.Contains(e.mousePosition))
            {
                if (e.shift) { inventory.RemoveAll(slotData.id); Message($"You drop all your {name.ToLower()}."); }
                else if (gear && e.button == 0) { var err = equipment.EquipFromInventory(i); Message(err ?? $"You {verb.ToLower()} the {name.ToLower()}."); }
                else { inventory.RemoveAt(i); Message($"You drop the {name.ToLower()}."); }
                e.Use();
            }
        }
        GUI.Label(new Rect(r.x, r.yMax - 26, pw, 20), $"{ThorvayneInventory.Size - inventory.FreeSlots} / {ThorvayneInventory.Size}      I / Tab to close", small);
        return r.x;
    }

    // RuneScape-style layout: a body outline of slots
    static readonly (string slot, int col, int row)[] EquipLayout =
    {
        ("Head", 1, 0), ("Cape", 0, 1), ("Neck", 1, 1), ("Ammo", 2, 1), ("Weapon", 0, 2), ("Body", 1, 2), ("Shield", 2, 2),
        ("Legs", 1, 3), ("Hands", 0, 4), ("Feet", 1, 4), ("Ring", 2, 4),
    };

    float EquipmentPanel(float right, float h)
    {
        const int size = 54, gapX = 22, gapY = 12;
        float gw = 3 * size + 2 * gapX, gh = 5 * size + 4 * gapY;
        float pw = gw + 40, ph = gh + 84;
        var r = new Rect(right - pw, h - ph - 10, pw, ph);
        var inner = Panel(r, "Equipment");
        float ox = inner.x + (inner.width - gw) / 2, oy = inner.y + 2;
        System.Func<int, int, Rect> At = (col, row) => new Rect(ox + col * (size + gapX), oy + row * (size + gapY), size, size);

        // brass lines joining the slots, like a body outline
        var lc = GUI.color; GUI.color = new Color(0.58f, 0.44f, 0.22f, 0.8f);
        var top = At(1, 0); var bottom = At(1, 4);
        GUI.DrawTexture(new Rect(top.center.x - 1, top.center.y, 3, bottom.center.y - top.center.y), Texture2D.whiteTexture);
        foreach (int row in new[] { 1, 2 })
        {
            var a = At(0, row); var b = At(2, row);
            GUI.DrawTexture(new Rect(a.center.x, a.center.y - 1, b.center.x - a.center.x, 3), Texture2D.whiteTexture);
        }
        var hl = At(0, 4); var rg = At(2, 4);
        GUI.DrawTexture(new Rect(hl.center.x, hl.center.y - 1, rg.center.x - hl.center.x, 3), Texture2D.whiteTexture);
        GUI.color = lc;

        foreach (var place in EquipLayout)
        {
            string slotName = place.slot;
            var c = At(place.col, place.row);
            var item = ThorvayneItems.Get(equipment.Get(slotName));
            bool over = ItemSlot(c, item != null ? item.id : null, 1);
            if (item == null)
            {
                var c0 = GUI.color; GUI.color = Faint;
                GUI.Label(c, slotName, small);
                GUI.color = c0;
                if (over) Tip(slotName, "Empty");
                continue;
            }
            if (over) Tip(item.name, ItemInfo(item, "Click: Remove"));
            var e = Event.current;
            if (e.type == EventType.MouseDown && c.Contains(e.mousePosition))
            {
                var err = equipment.Unequip(slotName);
                Message(err ?? $"You take off the {item.name.ToLower()}.");
                e.Use();
            }
        }
        GUI.Label(new Rect(r.x, r.yMax - 26, pw, 20), "G to close", small);
        return r.x;
    }

    void SkillsPanel(float right, float h)
    {
        var all = ThorvayneSkills.All;
        float pw = 300, ph = all.Length * 28 + 76;
        var r = new Rect(right - pw, h - ph - 10, pw, ph);
        var inner = Panel(r, $"Skills   Total {skills.TotalLevel}");
        for (int i = 0; i < all.Length; i++)
        {
            string s = all[i];
            int lvl = skills.Level(s);
            float xp = skills.Xp(s), a = ThorvayneSkills.XpForLevel(lvl), b = ThorvayneSkills.XpForLevel(lvl + 1);
            float t = lvl >= ThorvayneSkills.MaxLevel ? 1f : Mathf.Clamp01((xp - a) / Mathf.Max(1f, b - a));
            var row = new Rect(inner.x, inner.y + i * 28, inner.width, 24);
            bool over = row.Contains(Event.current.mousePosition);
            GUI.Box(row, GUIContent.none, over ? slotHover : slot);
            GUI.Label(new Rect(row.x + 8, row.y + 2, 110, 20), s, label);
            var lv = new GUIStyle(label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            lv.normal.textColor = Gold;
            GUI.Label(new Rect(row.x + 100, row.y + 2, 30, 20), lvl.ToString(), lv);
            var bb = new Rect(row.x + 140, row.y + 6, row.width - 148, 12);
            GUI.Box(bb, GUIContent.none, bar);
            if (t > 0.02f) GUI.Box(new Rect(bb.x, bb.y, Mathf.Max(6, bb.width * t), bb.height), GUIContent.none, barFill);
            if (over) Tip($"{s}  level {lvl}", lvl >= ThorvayneSkills.MaxLevel ? $"{xp:N0} XP" : $"{xp:N0} XP\n{b - xp:N0} XP to level {lvl + 1}");
        }
        GUI.Label(new Rect(r.x, r.yMax - 26, pw, 20), "K to close", small);
    }

    static void Shadowed(Rect r, string text, GUIStyle style)
    {
        var col = style.normal.textColor;
        style.normal.textColor = new Color(0, 0, 0, 0.8f * GUI.color.a);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
        style.normal.textColor = col;
        GUI.Label(r, text, style);
    }
}
