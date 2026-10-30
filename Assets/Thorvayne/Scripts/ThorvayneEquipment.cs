using System;
using System.Collections.Generic;
using UnityEngine;

// What the character is wearing and wielding, in RuneScape-style slots.
// Equip from the inventory (click the item), unequip from the equipment panel (G). Saved between plays.
// New characters start with a bronze axe in hand.
public class ThorvayneEquipment : MonoBehaviour
{
    public static readonly string[] Slots = { "Head", "Cape", "Neck", "Ammo", "Weapon", "Body", "Shield", "Legs", "Hands", "Feet", "Ring" };
    const string SaveKey = "Thorvayne.Equipment";

    public event Action Changed;
    readonly Dictionary<string, string> worn = new Dictionary<string, string>();
    ThorvayneInventory inventory;
    ThorvayneSkills skills;

    void Awake()
    {
        inventory = GetComponent<ThorvayneInventory>();
        skills = GetComponent<ThorvayneSkills>();
        if (PlayerPrefs.HasKey(SaveKey)) Load();
        else { worn["Weapon"] = "bronze_axe"; Save(); }   // starter axe
    }

    public string Get(string slot) { return worn.TryGetValue(slot, out var id) ? id : null; }

    // Wear the item in an inventory slot; whatever was in that equipment slot goes back to the inventory.
    // Returns a message to show, or null on success.
    public string EquipFromInventory(int index)
    {
        if (inventory == null) return "No inventory.";
        var s = inventory.slots[index];
        var it = s != null ? ThorvayneItems.Get(s.id) : null;
        if (it == null || it.slot == null) return "You can't wear that.";
        if (it.skill != null && skills != null && skills.Level(it.skill) < it.level)
            return $"You need a {it.skill} level of {it.level} to use the {it.name.ToLower()}.";
        string old = Get(it.slot);
        inventory.Put(index, old);            // swap (empty if nothing was worn)
        worn[it.slot] = it.id;
        Save(); Changed?.Invoke();
        return null;
    }

    public string Unequip(string slot)
    {
        string id = Get(slot);
        if (id == null) return null;
        if (inventory == null || !inventory.CanAdd(id)) return "You don't have enough inventory space.";
        inventory.Add(id);
        worn.Remove(slot);
        Save(); Changed?.Invoke();
        return null;
    }

    [Serializable] class SaveData { public List<string> slots = new List<string>(); public List<string> ids = new List<string>(); }

    void Save()
    {
        var d = new SaveData();
        foreach (var kv in worn) { d.slots.Add(kv.Key); d.ids.Add(kv.Value); }
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(d));
    }

    void Load()
    {
        worn.Clear();
        var d = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(SaveKey, "{}"));
        if (d == null) return;
        for (int i = 0; i < d.slots.Count && i < d.ids.Count; i++)
            if (ThorvayneItems.Get(d.ids[i]) != null) worn[d.slots[i]] = d.ids[i];
    }

    public static void ClearSave() { PlayerPrefs.DeleteKey(SaveKey); }
}
