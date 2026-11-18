using System.IO;

namespace BossMod;

// 讀壞（含部分略過）的檔在任何寫回之前留一份 <檔名>.bad-<時間>：逐位元組複製讀到的那份內容，
// CreateNew＝既有備份永不覆寫；同內容已有備份就不另存（存檔只在變動時發生，不改設定時每次載入都會走到這裡）。
// 永不擲例外；log 由呼叫端寫（各自的等級與後果說明）。
public static class UnreadableFileBackup
{
    public enum Result { Created, AlreadyBackedUp, Failed }

    public static Result Backup(string path, out string backupPath, out string error, DateTime? now = null)
    {
        backupPath = "";
        error = "";
        try
        {
            var original = File.ReadAllBytes(path);
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var name = Path.GetFileName(path);
            foreach (var old in new DirectoryInfo(dir).EnumerateFiles(name + ".bad-*"))
            {
                try
                {
                    if (old.Length == original.Length && File.ReadAllBytes(old.FullName).AsSpan().SequenceEqual(original))
                    {
                        backupPath = old.FullName;
                        return Result.AlreadyBackedUp;
                    }
                }
                catch (Exception)
                {
                    // 讀不到的舊備份不算「已備份過」，繼續比下一份
                }
            }

            backupPath = Path.Combine(dir, $"{name}.bad-{(now ?? DateTime.Now):yyyyMMdd-HHmmss-fff}");
            using (var fs = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                fs.Write(original);
            return Result.Created;
        }
        catch (Exception e)
        {
            error = $"{e.GetType().Name}: {e.Message}";
            return Result.Failed;
        }
    }
}
