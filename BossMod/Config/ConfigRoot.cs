using System.IO;
using System.Reflection;
using System.Text.Json;

namespace BossMod;

public sealed class ConfigRoot
{
    public Event Modified = new();
    public readonly Dictionary<Type, ConfigNode> _nodes = [];
    public List<ConfigNode> Nodes => [.. _nodes.Values];

    public void Initialize()
    {
        foreach (var t in Utils.GetDerivedTypes<ConfigNode>(Assembly.GetExecutingAssembly()).Where(t => !t.IsAbstract))
        {
            if (Activator.CreateInstance(t) is not ConfigNode inst)
            {
                Service.Log($"[Config] Failed to create an instance of {t}");
                continue;
            }
            inst.Modified.Subscribe(Modified.Fire);
            _nodes[t] = inst;
        }
    }

    public T Get<T>() where T : ConfigNode => (T)_nodes[typeof(T)];
    public T Get<T>(Type derived) where T : ConfigNode => (T)_nodes[derived];

    public ConfigListener<T> GetAndSubscribe<T>(Action<T> modified) where T : ConfigNode => new(Get<T>(), modified);

    public void LoadFromFile(FileInfo file)
    {
        // 版本轉換會 MoveTo 改掉 file 的路徑 ⇒ 先抄下原檔路徑
        var path = file.FullName;
        try
        {
            var data = ConfigConverter.Schema.Load(file);
            using var json = data.document;
            var ser = Serialization.BuildSerializationOptions();
            var failedNodes = 0;
            foreach (var jconfig in data.payload.EnumerateObject())
            {
                // 逐節點隔離：一個節點讀到一半擲例外，只有它中斷處之後的欄位留預設值，其他節點照常讀
                try
                {
                    var type = Type.GetType(jconfig.Name);
                    var node = type != null ? _nodes.GetValueOrDefault(type) : null;
                    node?.Deserialize(jconfig.Value, ser);
                }
                catch (Exception e)
                {
                    ++failedNodes;
                    Service.Logger?.Warning($"[Config] 設定節點 {jconfig.Name} 讀到一半失敗：這個節點中斷處之後的欄位用預設值，其他節點照常讀取。{e.GetType().Name}: {e.Message}");
                }
            }
            if (failedNodes != 0)
                BackupUnreadable(path, $"有 {failedNodes} 個設定節點沒有完整讀進來");
        }
        catch (Exception e)
        {
            // 全新安裝（沒有設定檔）照舊只記 Debug；檔案在卻讀不了＝使用者設定會回預設，要看得到
            if (File.Exists(path))
            {
                Service.Logger?.Warning($"Failed to load config from {path}: {e}");
                BackupUnreadable(path, "設定檔讀取失敗");
            }
            else
                Service.Log($"Failed to load config from {path}: {e}");
        }
    }

    // 讀檔失敗後下一次存檔會以預設值覆蓋原檔 ⇒ 先複製一份；複製失敗只記錄，不影響載入
    private static void BackupUnreadable(string path, string reason)
    {
        var name = Path.GetFileName(path);
        var after = $"之後下一次存檔（改任何設定時）就會用目前的值整份覆寫 {name}。";
        switch (UnreadableFileBackup.Backup(path, out var backup, out var error))
        {
            case UnreadableFileBackup.Result.Created:
                Service.Logger?.Warning($"[Config] {reason}，原檔已另存為 {backup}。{after}");
                break;
            case UnreadableFileBackup.Result.AlreadyBackedUp:
                Service.Logger?.Warning($"[Config] {reason}，相同內容先前已另存為 {backup}，這次不再另存。{after}");
                break;
            default:
                Service.Logger?.Warning($"[Config] {reason}，另存原檔也失敗: {error}。{after}");
                break;
        }
    }

    public void SaveToFile(FileInfo file)
    {
        string? tmp = null;
        try
        {
            var ser = Serialization.BuildSerializationOptions();
            var serializedNodes = new ConcurrentDictionary<Type, string>();

            Parallel.ForEach(_nodes, entry =>
            {
                using var ms = new MemoryStream();
                using var tempWriter = new Utf8JsonWriter(ms);
                entry.Value.Serialize(tempWriter, ser);
                tempWriter.Flush();
                serializedNodes[entry.Key] = Encoding.UTF8.GetString(ms.ToArray());
            });

            // 先寫完整的暫存檔、落盤後再一次換名蓋過原檔：寫到一半崩潰時原檔仍完整
            tmp = file.FullName + ".tmp";
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("Version", ConfigConverter.Schema.CurrentVersion);
                    writer.WritePropertyName("Payload");
                    writer.WriteStartObject();

                    foreach (var (t, jsonStr) in serializedNodes)
                    {
                        writer.WritePropertyName(t.FullName!);
                        writer.WriteRawValue(jsonStr);
                    }

                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                stream.Flush(true);
            }
            File.Move(tmp, file.FullName, true);
            tmp = null;
        }
        catch (Exception e)
        {
            Service.Log($"Failed to save config to {file.FullName}: {e}");
            if (tmp != null)
            {
                try { File.Delete(tmp); } catch { }
            }
        }
    }

    public List<string> ConsoleCommand(ReadOnlySpan<string> args, bool save = true)
    {
        List<string> result = [];
        if (args.Length == 0)
        {
            result.Add("Usage: /bmr cfg <config-type> <field> <value>");
            result.Add("Both config-type and field can be shortened. Valid config-types:");
            foreach (var t in _nodes.Keys)
                result.Add($"- {t.Name}");
        }
        else
        {
            // 名稱完全相同（不分大小寫）就只取那一個，不因為它同時是別的名稱的子字串而回「Ambiguous」
            List<ConfigNode> matchingNodes = [];
            foreach (var (t, n) in _nodes)
            {
                if (string.Equals(t.Name, args[0], StringComparison.CurrentCultureIgnoreCase))
                {
                    matchingNodes.Clear();
                    matchingNodes.Add(n);
                    break;
                }
                if (t.Name.Contains(args[0], StringComparison.CurrentCultureIgnoreCase))
                    matchingNodes.Add(n);
            }
            if (matchingNodes.Count == 0)
            {
                result.Add("Config type not found. Valid types:");
                foreach (var t in _nodes.Keys)
                    result.Add($"- {t.Name}");
            }
            else if (matchingNodes.Count > 1)
            {
                result.Add("Ambiguous config type, pass longer pattern. Matches:");
                foreach (var n in matchingNodes)
                    result.Add($"- {n.GetType().Name}");
            }
            else if (args.Length == 1)
            {
                result.Add("Usage: /bmr cfg <config-type> <field> <value>");
                result.Add($"Valid fields for {matchingNodes[0].GetType().Name}:");
                foreach (var f in matchingNodes[0].GetType().GetFields().Where(f => f.GetCustomAttribute<PropertyDisplayAttribute>() != null))
                    result.Add($"- {f.Name}");
            }
            else
            {
                List<FieldInfo> matchingFields = [];
                foreach (var f in matchingNodes[0].GetType().GetFields().Where(f => f.GetCustomAttribute<PropertyDisplayAttribute>() != null))
                {
                    if (string.Equals(f.Name, args[1], StringComparison.CurrentCultureIgnoreCase))
                    {
                        matchingFields.Clear();
                        matchingFields.Add(f);
                        break;
                    }
                    if (f.Name.Contains(args[1], StringComparison.CurrentCultureIgnoreCase))
                        matchingFields.Add(f);
                }
                if (matchingFields.Count == 0)
                {
                    result.Add($"Field not found {args[1]}, Valid fields:");
                    foreach (var f in matchingNodes[0].GetType().GetFields().Where(f => f.GetCustomAttribute<PropertyDisplayAttribute>() != null))
                        result.Add($"- {f.Name}");
                }
                else if (matchingFields.Count > 1)
                {
                    result.Add("Ambiguous field name, pass longer pattern. Matches:");
                    foreach (var f in matchingFields)
                        result.Add($"- {f.Name}");
                }
                /*else if (args.Count == 2)
                {
                    result.Add("Usage: /bmr cfg <config-type> <field> <value>");
                    result.Add($"Type of {matchingNodes[0].GetType().Name}.{matchingFields[0].Name} is {matchingFields[0].FieldType.Name}");
                }*/
                else
                {
                    try
                    {
                        if (args.Length == 2)
                            result.Add(matchingFields[0].GetValue(matchingNodes[0])?.ToString() ?? $"Failed to get value of '{args[2]}'");
                        else
                        {
                            var val = FromConsoleString(args[2], matchingFields[0].FieldType);
                            if (val == null)
                            {
                                result.Add($"Failed to convert '{args[2]}' to {matchingFields[0].FieldType}");
                            }
                            else
                            {
                                matchingFields[0].SetValue(matchingNodes[0], val);
                                if (save)
                                    matchingNodes[0].Modified.Fire();
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        if (args.Length == 2)
                            result.Add($"Failed to get value of {matchingNodes[0].GetType().Name}.{matchingFields[0].Name} : {e}");
                        else
                            result.Add($"Failed to set {matchingNodes[0].GetType().Name}.{matchingFields[0].Name} to {args[2]}: {e}");
                    }
                }
            }
        }
        return result;
    }

    private object? FromConsoleString(string str, Type t)
        => t == typeof(bool) ? bool.Parse(str)
        : t == typeof(float) ? float.Parse(str)
        : t == typeof(int) ? int.Parse(str)
        : t.IsAssignableTo(typeof(Enum)) ? Enum.Parse(t, str)
        : null;
}
