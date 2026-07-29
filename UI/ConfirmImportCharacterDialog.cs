namespace BetterMountRoulette.UI;

using BetterMountRoulette.UI.Base;

using Dalamud.Bindings.ImGui;

internal sealed class ConfirmImportCharacterDialog(ConfirmImportCharacterDialog.ImportHandler importHandler)
    : DialogWindow("Better Mount Roulette", ImGuiWindowFlags.Modal)
{
    private readonly ImportHandler _importHandler = importHandler;

    private bool _skipAsking;

    public delegate void ImportHandler(bool import, bool rememberAnswer);

    public override void Draw()
    {
        ImGui.Text("此角色沒有已儲存的設定。"u8);
        ImGui.Text("要為此角色匯入舊版設定嗎？"u8);
        _ = ImGui.Checkbox("記住我的回答，不要再次詢問。"u8, ref _skipAsking);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("之後仍可在設定中變更。"u8);
        }

        bool? result = null;
        if (ImGui.Button("是"u8))
        {
            result = true;

        }

        ImGui.SameLine();
        if (ImGui.Button("否"u8))
        {
            result = false;
        }

        if (result is bool value)
        {
            _importHandler(value, _skipAsking);
            IsOpen = false;
        }
    }
}
