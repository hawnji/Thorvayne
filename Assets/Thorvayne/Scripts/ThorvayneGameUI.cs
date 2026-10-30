using System.Collections.Generic;
using UnityEngine;

// Simple on-screen game UI: message box, action prompt, XP drops, level-ups,
// inventory (I or Tab), equipment (G) and skills (K) panels. The mouse is freed while a panel is open.
// In the inventory, click gear to wear or wield it, click anything else (or right-click) to drop it,
// Shift-click to drop all of that kind. In the equipment panel, click an item to take it off.
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

    void Styles()
    {
        if (label != null) return;
        label = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
        label.normal.textColor = Color.white;
        small = new GUIStyle(label) { fontSize = 11, alignment = TextAnchor.LowerCenter, wordWrap = false };
        centre = new GUIStyle(label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        big = new GUIStyle(centre) { fontSize = 22 };
        big.normal.textColor = new Color(1f, 0.85f, 0.35f);
    }

    void OnGUI()
    {
        Styles();
        float w = Screen.width, h = Screen.height;

        // message box (bottom left)
        GUI.Box(new Rect(10, h - 150, 470, 140), "");
        for (int i = 0; i < messages.Count; i++)
            GUI.Label(new Rect(18, h - 146 + i * 22, 460, 22), messages[i], label);

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

        if (showInventory) InventoryPanel(w, h);
        if (showSkills) SkillsPanel(w, h);
        if (showEquipment && equipment != null) EquipmentPanel(w, h);
    }

    void InventoryPanel(float w, float h)
    {
        const int cols = 4, size = 52, gap = 4;
        int rows = ThorvayneInventory.Size / cols;
        float pw = cols * (size + gap) + gap + 16, ph = rows * (size + gap) + gap + 54;
        var r = new Rect(w - pw - 10, h - ph - 10, pw, ph);
        GUI.Box(r, $"Inventory  ({ThorvayneInventory.Size - inventory.FreeSlots}/{ThorvayneInventory.Size})");
        string hover = "";
        for (int i = 0; i < ThorvayneInventory.Size; i++)
        {
            var cell = new Rect(r.x + 8 + gap + (i % cols) * (size + gap), r.y + 26 + (i / cols) * (size + gap), size, size);
            GUI.Box(cell, "");
            var slot = inventory.slots[i];
            if (slot == null) continue;
            var item = ThorvayneItems.Get(slot.id);
            var c = GUI.color; GUI.color = item != null ? item.color : Color.magenta;
            GUI.DrawTexture(new Rect(cell.x + 8, cell.y + 12, size - 16, size - 26), Texture2D.whiteTexture);
            GUI.color = c;
            string name = item != null ? item.name : slot.id;
            GUI.Label(new Rect(cell.x, cell.y, size, size - 2), slot.count > 1 ? $"{ShortName(name)} x{slot.count}" : ShortName(name), small);
            bool gear = item != null && item.slot != null && equipment != null;
            if (cell.Contains(Event.current.mousePosition)) hover = gear ? $"{name}  (click: {(item.slot == "Weapon" ? "wield" : "wear")}, right-click: drop)" : $"{name}  (click: drop, Shift: drop all)";
            var e = Event.current;
            if (e.type == EventType.MouseDown && cell.Contains(e.mousePosition))
            {
                if (e.shift) { inventory.RemoveAll(slot.id); Message($"You drop all your {name.ToLower()}."); }
                else if (gear && e.button == 0) { var err = equipment.EquipFromInventory(i); Message(err ?? $"You {(item.slot == "Weapon" ? "wield" : "wear")} the {name.ToLower()}."); }
                else { inventory.RemoveAt(i); Message($"You drop the {name.ToLower()}."); }
                e.Use();
            }
        }
        GUI.Label(new Rect(r.x + 8, r.yMax - 26, pw - 16, 22), hover != "" ? hover : "I / Tab to close", small);
    }

    // RuneScape-style layout: a body outline of slots
    static readonly (string slot, int col, int row)[] EquipLayout =
    {
        ("Head", 1, 0), ("Cape", 0, 1), ("Neck", 1, 1), ("Ammo", 2, 1), ("Weapon", 0, 2), ("Body", 1, 2), ("Shield", 2, 2),
        ("Legs", 1, 3), ("Hands", 0, 4), ("Feet", 1, 4), ("Ring", 2, 4),
    };

    void EquipmentPanel(float w, float h)
    {
        const int size = 52, gap = 8;
        float pw = 3 * (size + gap) + gap + 16, ph = 5 * (size + gap) + gap + 54;
        var r = new Rect(w - 254 - 10 - pw, h - ph - 10, pw, ph);
        GUI.Box(r, "Equipment");
        string hover = "";
        foreach (var place in EquipLayout)
        {
            string slot = place.slot;
            var cell = new Rect(r.x + 8 + gap + place.col * (size + gap), r.y + 26 + place.row * (size + gap), size, size);
            GUI.Box(cell, "");
            var item = ThorvayneItems.Get(equipment.Get(slot));
            if (item == null)
            {
                var c0 = GUI.color; GUI.color = new Color(1, 1, 1, 0.45f);
                GUI.Label(new Rect(cell.x, cell.y, size, size - 18), slot, small);
                GUI.color = c0;
                if (cell.Contains(Event.current.mousePosition)) hover = $"{slot}: empty";
                continue;
            }
            var c = GUI.color; GUI.color = item.color;
            GUI.DrawTexture(new Rect(cell.x + 8, cell.y + 12, size - 16, size - 26), Texture2D.whiteTexture);
            GUI.color = c;
            GUI.Label(new Rect(cell.x, cell.y, size, size - 2), ShortName(item.name), small);
            if (cell.Contains(Event.current.mousePosition))
                hover = item.skill != null ? $"{item.name} ({item.skill} {item.level})  click: remove" : $"{item.name}  click: remove";
            var e = Event.current;
            if (e.type == EventType.MouseDown && cell.Contains(e.mousePosition))
            {
                var err = equipment.Unequip(slot);
                Message(err ?? $"You take off the {item.name.ToLower()}.");
                e.Use();
            }
        }
        GUI.Label(new Rect(r.x + 8, r.yMax - 26, pw - 16, 22), hover != "" ? hover : "G to close", small);
    }

    static string ShortName(string n) { int i = n.LastIndexOf(' '); return i > 0 ? n.Substring(0, i) : n; }

    void SkillsPanel(float w, float h)
    {
        var all = ThorvayneSkills.All;
        float pw = 300, ph = all.Length * 26 + 60;
        var r = new Rect(w - pw - 474, h - ph - 10, pw, ph);
        GUI.Box(r, $"Skills   (total level {skills.TotalLevel})");
        for (int i = 0; i < all.Length; i++)
        {
            string s = all[i];
            int lvl = skills.Level(s);
            float xp = skills.Xp(s), a = ThorvayneSkills.XpForLevel(lvl), b = ThorvayneSkills.XpForLevel(lvl + 1);
            float t = lvl >= ThorvayneSkills.MaxLevel ? 1f : Mathf.Clamp01((xp - a) / Mathf.Max(1f, b - a));
            var row = new Rect(r.x + 10, r.y + 28 + i * 26, pw - 20, 22);
            var c = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.35f); GUI.DrawTexture(new Rect(row.x + 130, row.y + 4, 150, 14), Texture2D.whiteTexture);
            GUI.color = new Color(0.3f, 0.75f, 0.35f); GUI.DrawTexture(new Rect(row.x + 130, row.y + 4, 150 * t, 14), Texture2D.whiteTexture);
            GUI.color = c;
            GUI.Label(row, $"{s}", label);
            GUI.Label(new Rect(row.x + 96, row.y, 40, 22), $"{lvl}", label);
            if (row.Contains(Event.current.mousePosition))
                GUI.Label(new Rect(r.x + 10, r.yMax - 28, pw - 20, 22), lvl >= ThorvayneSkills.MaxLevel ? $"{s}: {xp:N0} XP" : $"{s}: {xp:N0} XP, {b - xp:N0} to level {lvl + 1}", label);
        }
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
