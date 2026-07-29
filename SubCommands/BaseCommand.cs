namespace BetterMountRoulette.SubCommands;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;

[SuppressMessage("Performance", "CA1812", Justification = "Instantiated via reflection")]
internal sealed class BaseCommand : SubCommandBase
{
    private string? _helpMessage;

    public override string HelpMessage => _helpMessage ??= BuildHelpMessage();

    public override string CommandName => "";

    protected override bool ExecuteInternal(string[] parameter)
    {
        if (parameter.Length > 0)
        {
            return false;
        }

        Plugin.WindowManager.OpenConfigWindow();
        return true;
    }

    private string BuildHelpMessage()
    {
        StringBuilder sb = new StringBuilder()
            .AppendLine("用法：")
            .AppendLine(FullCommand)
            .AppendLine("  -> 開啟設定視窗")
            .Append(FullCommand).AppendLine(" help")
            .Append("  -> 顯示此說明");

        string[] modes = SubCommands.Keys.Where(x => !string.IsNullOrEmpty(x))
            .Select(x => x.ToLower(CultureInfo.CurrentCulture)).ToArray();
        if (modes.Length == 0)
        {
            _ = sb.AppendLine()
                .Append(FullCommand).AppendLine(" <mode> [help]")
                .AppendLine("  -> 執行所選模式。可用模式：")
                .Append("  -> ")
                .AppendLine(string.Join(", ", modes))
                .AppendLine("  -> 若包含 help 參數，改為顯示所選模式的詳細資訊")
                .Append(CultureInfo.InvariantCulture, $"  -> 例如：{FullCommand} {modes[0]} help");
        }

        return sb.ToString();
    }
}
