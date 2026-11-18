using Dalamud.Utility;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BossMod;

// utility for loading versioned json configuration files, executing conversion if needed
public sealed class VersionedJSONSchema
{
    public delegate JsonNode ConvertDelegate(JsonNode input, int startingVersion, FileInfo path);

    public readonly int MinSupportedVersion;
    public readonly List<ConvertDelegate> Converters = [];

    public int CurrentVersion => MinSupportedVersion + Converters.Count;

    public (JsonDocument document, JsonElement payload) Load(FileInfo file)
    {
        var json = Serialization.ReadJson(file.FullName);
        var version = json.RootElement.TryGetProperty("version", out var jver) || json.RootElement.TryGetProperty("Version", out jver) ? jver.GetInt32() : 0;
        if (version < MinSupportedVersion)
            throw new ArgumentException($"Config file {file.FullName} version {version} is older than supported {MinSupportedVersion}");
        if (version > CurrentVersion)
            throw new ArgumentException($"Config file {file.FullName} version {version} is newer than supported {CurrentVersion}");
        if (!json.RootElement.TryGetProperty("payload", out var jpayload) && !json.RootElement.TryGetProperty("Payload", out jpayload))
            throw new ArgumentException($"Config file {file.FullName} does not contain a payload");

        // fast path: if file is of correct version, we're done
        if (version == CurrentVersion)
            return (json, jpayload);

        // execute the conversion
        JsonNode converted = jpayload.ValueKind switch
        {
            JsonValueKind.Object => JsonObject.Create(jpayload)!,
            JsonValueKind.Array => JsonArray.Create(jpayload)!,
            _ => throw new ArgumentException($"Config file {file.FullName} has unsupported payload type {jpayload.ValueKind}")
        };
        for (int i = version - MinSupportedVersion; i < Converters.Count; ++i)
            converted = Converters[i](converted, version, file);

        // backup the old version and write out new one
        var original = new FileInfo(file.FullName);
        var backup = new FileInfo(file.FullName + $".v{version}");
        if (!backup.Exists)
            file.MoveTo(backup.FullName);
        Save(original, jwriter => converted.WriteTo(jwriter));
        json.Dispose();

        // and now read again...
        json = Serialization.ReadJson(original.FullName);
        return (json, json.RootElement.GetProperty("payload"));
    }

    // 先序列化完整內容、再原子換名：序列化擲例外或寫到一半被結束，原檔都還是上一份完整內容。
    // 換名在「別人開著原檔」時擲 Win32Exception（舊的 FileMode.Create 在對方分享寫入時會成功）⇒ 呼叫端必須 catch。
    public void Save(FileInfo file, Action<Utf8JsonWriter> writePayload) => FilesystemUtil.WriteAllBytesSafe(file.FullName, Serialize(writePayload));

    public byte[] Serialize(Action<Utf8JsonWriter> writePayload)
    {
        using var ms = new MemoryStream();
        using (var jwriter = Serialization.WriteJson(ms))
        {
            jwriter.WriteStartObject();
            jwriter.WriteNumber("version", CurrentVersion);
            jwriter.WritePropertyName("payload");
            writePayload(jwriter);
            jwriter.WriteEndObject();
        }
        return ms.ToArray();
    }
}
