using System;
using System.Collections.Generic;
using UnityEngine;

// Skill levels 1-99 with RuneScape-style XP. Progress is saved between play sessions
// (Thorvayne > Reset Test Player Progress clears it).
public class ThorvayneSkills : MonoBehaviour
{
    public static readonly string[] All = { "Woodcutting", "Mining", "Fishing", "Hunting", "Farming", "Cooking", "Alchemy",
                                            "Smithing", "Tailoring", "Trading", "Thieving", "Agility", "Slayer", "Archaeology", "Waycraft" };
    public const int MaxLevel = 99;
    const string SaveKey = "Thorvayne.Skills";

    // skill, xp gained, new level if it went up (0 otherwise)
    public event Action<string, float, int> XpGained;

    readonly Dictionary<string, float> xp = new Dictionary<string, float>();
    static float[] table;

    void Awake() { Load(); }

    public float Xp(string skill) { return xp.TryGetValue(skill, out float v) ? v : 0f; }
    public int Level(string skill) { return LevelForXp(Xp(skill)); }
    public int TotalLevel { get { int t = 0; foreach (var s in All) t += Level(s); return t; } }

    public void AddXp(string skill, float amount)
    {
        int before = Level(skill);
        xp[skill] = Mathf.Min(Xp(skill) + amount, 200_000_000f);
        int after = Level(skill);
        Save();
        XpGained?.Invoke(skill, amount, after > before ? after : 0);
    }

    // XP needed to reach a level (level 2 = 83, level 99 = 13,034,431), as in RuneScape
    public static float XpForLevel(int level)
    {
        if (table == null)
        {
            table = new float[MaxLevel + 2];
            double points = 0;
            for (int l = 1; l <= MaxLevel + 1; l++)
            {
                table[l] = (float)Math.Floor(points / 4);
                points += Math.Floor(l + 300 * Math.Pow(2, l / 7.0));
            }
        }
        return table[Mathf.Clamp(level, 1, MaxLevel + 1)];
    }

    public static int LevelForXp(float x)
    {
        int l = 1;
        while (l < MaxLevel && x >= XpForLevel(l + 1)) l++;
        return l;
    }

    [Serializable] class SaveData { public List<string> names = new List<string>(); public List<float> xp = new List<float>(); }

    void Save()
    {
        var d = new SaveData();
        foreach (var kv in xp) { d.names.Add(kv.Key); d.xp.Add(kv.Value); }
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(d));
    }

    void Load()
    {
        xp.Clear();
        var d = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(SaveKey, "{}"));
        if (d == null) return;
        for (int i = 0; i < d.names.Count && i < d.xp.Count; i++) xp[d.names[i]] = d.xp[i];
    }

    public static void ClearSave() { PlayerPrefs.DeleteKey(SaveKey); }
}
