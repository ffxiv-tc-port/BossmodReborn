using System.IO;
using System.Text.Json;

namespace BossMod.Autorotation;

// note: presets in the database are immutable (otherwise eg. manager won't see the changes in active preset)
public sealed class PresetDatabase
{
    private readonly AutorotationConfig _cfg = Service.Config.Get<AutorotationConfig>();

    public readonly List<Preset> DefaultPresets; // default presets, distributed as part of the plugin
    public readonly List<Preset> UserPresets; // user-defined presets, stored in user's preset db
    public Event<Preset?, Preset?> PresetModified = new(); // (old, new); old == null if preset is added, new == null if preset is removed

    private readonly FileInfo _dbPath;

    public List<Preset> AllPresets
    {
        get
        {
            var countD = DefaultPresets.Count;
            var countU = UserPresets.Count;
            List<Preset> presets = new(countD + countU);
            for (var i = 0; i < countD; ++i)
            {
                var def = DefaultPresets[i];
                if (def.HiddenByDefault == _cfg.HideDefaultPreset)
                {
                    presets.Add(def);
                }
            }
            for (var i = 0; i < countU; ++i)
            {
                presets.Add(UserPresets[i]);
            }
            return presets;
        }
    }

    public PresetDatabase(string rootPath, FileInfo defaultPresets)
    {
        _dbPath = new(rootPath + ".db.json");
        BackupUserPresetsOnce();
        DefaultPresets = LoadPresetsFromFile(defaultPresets);
        UserPresets = LoadPresetsFromFile(_dbPath);
    }

    /// <summary>
    /// 第一次載入時，把使用者的 preset 資料庫逐位元組複製一份備份。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>為什麼需要</b>：策略軌的<b>序列化鍵</b>（track／option 的 InternalName）
    /// 一旦改變，舊存檔裡的字串就對不上，載入時 <c>FindIndex</c> 回 -1，
    /// 那一條設定<b>靜默消失</b>，而且存檔一被覆寫就再也救不回來。
    /// 循環框架改版必然會動到這些鍵，所以在任何載入發生之前先留一份原檔。
    /// <para>
    /// 📌 <b>只做一次</b>：備份檔已存在就不再覆寫 —— 否則第二次啟動會拿「已經失效的新檔」
    /// 蓋掉「還完好的舊備份」，那比沒有備份更糟。
    /// </para>
    /// <para>
    /// ⚠️ 逐位元組複製，不解析也不重新序列化：這份備份的用途是讓使用者手動還原，
    /// 經過我們的序列化器就不再是原檔了。
    /// </para>
    /// </remarks>
    private void BackupUserPresetsOnce()
    {
        try
        {
            if (!_dbPath.Exists)
                return;

            var backup = new FileInfo(Path.Combine(
                _dbPath.DirectoryName ?? "",
                Path.GetFileNameWithoutExtension(_dbPath.Name) + ".v1-backup.json"));
            if (backup.Exists)
                return;

            File.Copy(_dbPath.FullName, backup.FullName);
            // 使用者跑 LogLevel 2，要他看得到才有意義
            Service.Logger.Information(
                $"[Autorotation] 循環預設資料庫已備份到 {backup.FullName}。" +
                "循環框架改版會讓舊的預設內容失效、需要重新建立；這份是改版前的原檔，只會產生一次。");
        }
        catch (Exception ex)
        {
            // 🔴 備份失敗不可以擋住外掛載入，但一定要說出來 —— 否則使用者會以為自己有備份
            Service.Logger.Information($"[Autorotation] 備份循環預設資料庫失敗（將繼續載入，但沒有備份）: {ex}");
        }
    }

    private List<Preset> LoadPresetsFromFile(FileInfo file)
    {
        try
        {
            var data = PlanPresetConverter.PresetSchema.Load(file);
            using var json = data.document;
            return data.payload.Deserialize<List<Preset>>(Serialization.BuildSerializationOptions()) ?? [];
        }
        catch (Exception ex)
        {
            Service.Log($"Failed to parse preset database '{file.FullName}': {ex}");
            return [];
        }
    }

    // if index >= 0: replace or delete
    // if index == -1: add (if replacement is non-null) or notify about reordering (otherwise)
    public void Modify(int index, Preset? replacement)
    {
        var previous = index >= 0 ? UserPresets[index] : null;

        if (index < 0 && replacement != null)
            UserPresets.Add(replacement);
        else if (index >= 0 && replacement == null)
            UserPresets.RemoveAt(index);
        else if (index >= 0 && replacement != null)
            UserPresets[index] = replacement;

        if (previous != null || replacement != null)
            PresetModified.Fire(previous, replacement);

        Save();
    }

    public void Save()
    {
        try
        {
            PlanPresetConverter.PresetSchema.Save(_dbPath, jwriter => JsonSerializer.Serialize(jwriter, UserPresets, Serialization.BuildSerializationOptions()));
            Service.Log($"Database saved successfully to '{_dbPath.FullName}'");
        }
        catch (Exception ex)
        {
            Service.Log($"Failed to write database to '{_dbPath.FullName}': {ex}");
        }
    }

    public List<Preset> PresetsForClass(Class c)
    {
        var visible = AllPresets;
        var count = visible.Count;
        List<Preset> presets = new(count);
        for (var i = 0; i < count; ++i)
        {
            var vis = visible[i];
            var pm = vis.Modules;
            var countM = pm.Count;
            for (var j = 0; j < countM; ++j)
            {
                var pmj = pm[j];
                if (pmj.Definition.Classes[(int)c])
                {
                    presets.Add(vis);
                    break;
                }
            }
        }
        return presets;
    }

    public Preset? FindPresetByName(ReadOnlySpan<char> name, StringComparison cmp = StringComparison.CurrentCultureIgnoreCase)
    {
        foreach (var p in AllPresets)
            if (name.Equals(p.Name, cmp))
                return p;
        return null;
    }
}
