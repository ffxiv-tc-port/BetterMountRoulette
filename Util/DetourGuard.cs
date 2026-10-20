namespace BetterMountRoulette.Util;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

// detour 是遊戲原生碼呼叫進來的：受管理例外穿出去＝整個遊戲崩潰，所以 detour 裡的插件邏輯一律攔下。
// 每個功能只在第一次出事時記一行 Error、在聊天說一次；可從任何執行緒呼叫（聊天轉到框架執行緒）。
internal static class DetourGuard
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Design", "CA1031", Justification = "Called from hook detours: no exception may propagate into native code")]
    internal static void ReportOnce(PluginServices services, ref int reported, string logMessage, string chatMessage, Exception? ex)
    {
        if (Interlocked.Exchange(ref reported, 1) != 0)
        {
            return;
        }

        try
        {
            services.PluginLog.Error(ex, logMessage);
            _ = services.Framework.Run(() => services.Chat.PrintError($"[BetterMountRoulette] {chatMessage}若遊戲剛改版，請等插件更新。"));
        }
        catch
        {
            // 回報本身也不可以把例外送回原生框架。
        }
    }
}
