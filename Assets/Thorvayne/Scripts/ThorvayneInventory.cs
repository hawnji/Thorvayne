using System;
using System.Collections.Generic;
using UnityEngine;

// The items the game knows about. Add new ones here.
public static class ThorvayneItems
{
    public class Item
    {
        public string id, name;
        public Color color;
        public bool stackable;
        public string slot;     // equipment slot (ThorvayneEquipment.Slots), or null if it can't be worn
        public string skill;    // skill this tool or gear needs, e.g. Woodcutting for axes
        public int level = 1;   // level needed in that skill to use it
        public int power;       // tool tier: better axes chop faster
    }

    static readonly Dictionary<string, Item> items = new Dictionary<string, Item>();
    static void Add(string id, string name, Color color, bool stackable = false) { items[id] = new Item { id = id, name = name, color = color, stackable = stackable }; }
    static void Gear(string id, string name, Color color, string slot, string skill, int level, int power)
    {
        items[id] = new Item { id = id, name = name, color = color, slot = slot, skill = skill, level = level, power = power };
    }

    static ThorvayneItems()
    {
        Add("logs", "Logs", new Color(0.55f, 0.38f, 0.22f));
        Add("pine_logs", "Pine logs", new Color(0.66f, 0.5f, 0.3f));
        Add("palm_logs", "Palm logs", new Color(0.8f, 0.66f, 0.42f));
        Add("swamp_logs", "Swampwood logs", new Color(0.35f, 0.3f, 0.2f));
        Add("frost_logs", "Frost pine logs", new Color(0.72f, 0.8f, 0.86f));

        // axes: wielded in the Weapon slot (or carried in the inventory) to chop trees
        Gear("bronze_axe", "Bronze axe", new Color(0.72f, 0.46f, 0.24f), "Weapon", "Woodcutting", 1, 1);
        Gear("iron_axe", "Iron axe", new Color(0.45f, 0.45f, 0.48f), "Weapon", "Woodcutting", 1, 2);
        Gear("steel_axe", "Steel axe", new Color(0.72f, 0.74f, 0.77f), "Weapon", "Woodcutting", 6, 3);
        Gear("mithril_axe", "Mithril axe", new Color(0.33f, 0.38f, 0.7f), "Weapon", "Woodcutting", 21, 4);
        Gear("adamant_axe", "Adamant axe", new Color(0.3f, 0.55f, 0.35f), "Weapon", "Woodcutting", 31, 5);
        Gear("rune_axe", "Rune axe", new Color(0.3f, 0.7f, 0.82f), "Weapon", "Woodcutting", 41, 6);
    }

    public static Item Get(string id) { return id != null && items.TryGetValue(id, out var it) ? it : null; }
    public static bool IsAxe(Item it) { return it != null && it.skill == "Woodcutting" && it.power > 0; }
}

// 28 slots, like RuneScape. Logs take one slot each; stackable items share a slot.
// Saved between play sessions (Thorvayne > Reset Test Player Progress clears it).
public class ThorvayneInventory : MonoBehaviour
{
    public const int Size = 28;
    const string SaveKey = "Thorvayne.Inventory";

    [Serializable] public class Slot { public string id; public int count; }
    public readonly Slot[] slots = new Slot[Size];

    void Awake() { Load(); }

    public bool IsFull => FreeSlots == 0;
    public int FreeSlots { get { int n = 0; foreach (var s in slots) if (s == null) n++; return n; } }

    public int Count(string id) { int n = 0; foreach (var s in slots) if (s != null && s.id == id) n += s.count; return n; }

    public bool CanAdd(string id)
    {
        var it = ThorvayneItems.Get(id);
        if (it != null && it.stackable) foreach (var s in slots) if (s != null && s.id == id) return true;
        return !IsFull;
    }

    public bool Add(string id, int count = 1)
    {
        var it = ThorvayneItems.Get(id);
        if (it == null) { Debug.LogWarning($"Unknown item '{id}'"); return false; }
        if (it.stackable)
        {
            foreach (var s in slots) if (s != null && s.id == id) { s.count += count; Save(); return true; }
            count = Mathf.Max(count, 1);
            for (int i = 0; i < Size; i++) if (slots[i] == null) { slots[i] = new Slot { id = id, count = count }; Save(); return true; }
            return false;
        }
        int added = 0;
        for (int i = 0; i < Size && added < count; i++)
            if (slots[i] == null) { slots[i] = new Slot { id = id, count = 1 }; added++; }
        if (added > 0) Save();
        return added == count;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= Size || slots[index] == null) return;
        slots[index] = null;
        Save();
    }

    public int FirstFree() { for (int i = 0; i < Size; i++) if (slots[i] == null) return i; return -1; }

    public void Put(int index, string id, int count = 1)
    {
        slots[index] = id == null ? null : new Slot { id = id, count = count };
        Save();
    }

    public void RemoveAll(string id)
    {
        for (int i = 0; i < Size; i++) if (slots[i] != null && slots[i].id == id) slots[i] = null;
        Save();
    }

    [Serializable] class SaveData { public List<string> ids = new List<string>(); public List<int> counts = new List<int>(); }

    void Save()
    {
        var d = new SaveData();
        foreach (var s in slots) { d.ids.Add(s == null ? "" : s.id); d.counts.Add(s == null ? 0 : s.count); }
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(d));
    }

    void Load()
    {
        var d = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(SaveKey, "{}"));
        if (d == null) return;
        for (int i = 0; i < Size && i < d.ids.Count && i < d.counts.Count; i++)
            slots[i] = string.IsNullOrEmpty(d.ids[i]) || ThorvayneItems.Get(d.ids[i]) == null ? null : new Slot { id = d.ids[i], count = d.counts[i] };
    }

    public static void ClearSave() { PlayerPrefs.DeleteKey(SaveKey); }
}
