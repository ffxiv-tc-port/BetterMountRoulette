namespace BetterMountRoulette.Util;

using Dalamud.Plugin.Services;
using Dalamud.Utility;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;

// 本檔契約（艦隊範本 config-load-safety）：讀壞的原檔在被覆寫之前先複製成 <檔名>.bad-<時間>；
// 既有備份永不覆寫（CreateNew）；同內容的備份只留一份；寫入一律 WriteAllTextSafe。兩個方法都永不擲例外。
internal static class ConfigFileSafety
{
    // 回傳這份內容的備份檔名（含「早已備份過」的那一份）；失敗回 null。
    [SuppressMessage("Design", "CA1031", Justification = "Backup is best-effort and must never block loading")]
    public static string? BackupUnreadable(string path, string reason, string consequence, IPluginLog log, Func<DateTime>? now = null)
    {
        string name = Path.GetFileName(path);
        try
        {
            byte[] original = File.ReadAllBytes(path);
            string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            foreach (FileInfo old in new DirectoryInfo(dir).EnumerateFiles(name + ".bad-*"))
            {
                if (old.Length == original.Length && File.ReadAllBytes(old.FullName).AsSpan().SequenceEqual(original))
                {
                    log.Information("[設定] {File}：{Reason}；相同內容已備份過（{Backup}），這次不再另存。{Consequence}", name, reason, old.FullName, consequence);
                    return old.Name;
                }
            }

            string backup = Path.Combine(dir, string.Create(CultureInfo.InvariantCulture, $"{name}.bad-{(now ?? (() => DateTime.Now))():yyyyMMdd-HHmmss-fff}"));
            using (FileStream fs = new(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(original);
            }

            log.Information("[設定] {File}：{Reason}；原檔已備份為 {Backup}。{Consequence}", name, reason, backup, consequence);
            return Path.GetFileName(backup);
        }
        catch (Exception e)
        {
            log.Information("[設定] {File}：{Reason}；備份原檔失敗（{Type}: {Error}）。{Consequence}", name, reason, e.GetType().Name, e.Message, consequence);
            return null;
        }
    }

    // 失敗只記錄、回 false，原檔維持上一次的完整內容。
    [SuppressMessage("Design", "CA1031", Justification = "Save failures are logged; callers run in login handlers and window close")]
    public static bool Save(string path, string text, IPluginLog log)
    {
        try
        {
            FilesystemUtil.WriteAllTextSafe(path, text);
            return true;
        }
        catch (Exception e)
        {
            log.Warning("[設定] 寫入 {File} 失敗（{Type}: {Error}），原檔維持上一次的內容。", Path.GetFileName(path), e.GetType().Name, e.Message);
            return false;
        }
    }
}
