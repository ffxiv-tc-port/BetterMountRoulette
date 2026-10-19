namespace BetterMountRoulette.UI;

using BetterMountRoulette.Config.Data;
using BetterMountRoulette.Util;

using Dalamud.Interface.Windowing;

using Dalamud.Bindings.ImGui;

using Lumina.Excel.Sheets;

using System;
using System.Linq;
using System.Numerics;

internal sealed class ConfigWindow : Window
{
    private readonly BetterMountRoulettePlugin _plugin;
    private readonly PluginServices _services;
    private readonly MountGroupPage _mountGroupPage;
    private readonly CharacterManagementRenderer _charManagementRenderer;
    private float _windowMinWidth;

    private static (uint RowId, string Name)[]? _mainCommands;

    public ConfigWindow(BetterMountRoulettePlugin plugin, PluginServices services) : base("Better Mount Roulette 設定", ImGuiWindowFlags.AlwaysAutoResize)
    {
        _plugin = plugin;
        _services = services;
        _mountGroupPage = new MountGroupPage(_plugin, services);
        _charManagementRenderer = new CharacterManagementRenderer(
            services,
            _plugin.WindowManager,
            _plugin.CharacterManager,
            _plugin.Configuration);
    }

    public override int GetHashCode()
    {
        return 0;
    }

    public override bool Equals(object? obj)
    {
        return obj is ConfigWindow;
    }

    public override void OnOpen()
    {
        base.OnOpen();
        _plugin.MountRegistry.RefreshUnlocked();
    }

    public override void PreDraw()
    {
        base.PreDraw();
        ImGui.SetNextWindowSizeConstraints(new Vector2(_windowMinWidth, 0), new Vector2(float.MaxValue, float.MaxValue));
    }

    public override void Draw()
    {
        if (_plugin.CharacterConfig is not CharacterConfig characterConfig)
        {
            ImGui.Text("請先登入角色"u8);
        }
        else if (ImGui.BeginTabBar("settings"u8))
        {
            Tab("一般"u8, GeneralConfigTab);
            Tab("坐騎群組"u8, _mountGroupPage.RenderPage);
            Tab("角色管理"u8, x => _charManagementRenderer.Draw());

            ImGui.EndTabBar();

            // Helper method for reducing boilerplate
            void Tab(ReadOnlySpan<byte> name, Action<CharacterConfig> contentSelector)
            {
                if (ImGui.BeginTabItem(name))
                {
                    contentSelector(characterConfig);
                    ImGui.EndTabItem();
                }
            }
        }

        _windowMinWidth = ImGui.GetWindowWidth();
    }

    public override void OnClose()
    {
        base.OnClose();
        _plugin.CharacterManager.SaveCurrentCharacterConfig();
        _plugin.SaveConfig(_plugin.Configuration);
        _plugin.WindowManager.RemoveWindow(this);
    }

    private void GeneralConfigTab(CharacterConfig characterConfig)
    {
        string? mountRouletteGroupName = characterConfig.MountRouletteGroup;
        string? flyingRouletteGroupName = characterConfig.FlyingMountRouletteGroup;

        bool revealMountsNormal = characterConfig.RevealMountsNormal;
        bool revealMountsFlying = characterConfig.RevealMountsFlying;

        RouletteGroup(characterConfig, ref mountRouletteGroupName, ref revealMountsNormal);
        RouletteGroup(characterConfig, ref flyingRouletteGroupName, ref revealMountsFlying, isFlying: true);

        ImGui.Text("選定的群組至少要啟用一隻坐騎，取代設定才會生效。"u8);

        EnableFlyingRouletteButtonCheckbox(characterConfig);

        bool suppressChatErrors = characterConfig.SuppressChatErrors;
        _ = ImGui.Checkbox("不在聊天欄顯示錯誤訊息"u8, ref suppressChatErrors);

        characterConfig.MountRouletteGroup = mountRouletteGroupName;
        characterConfig.FlyingMountRouletteGroup = flyingRouletteGroupName;
        characterConfig.RevealMountsNormal = revealMountsNormal;
        characterConfig.RevealMountsFlying = revealMountsFlying;
        characterConfig.SuppressChatErrors = suppressChatErrors;

        // backwards compatibility
        _plugin.Configuration.Enabled = (mountRouletteGroupName ?? flyingRouletteGroupName) is not null;
    }

    private void EnableFlyingRouletteButtonCheckbox(CharacterConfig characterConfig)
    {
        bool enableFlyingRouletteButton = characterConfig.EnableFlyingRouletteButton;
        if (ImGui.Checkbox("重新啟用「飛行坐騎隨機召喚」按鈕"u8, ref enableFlyingRouletteButton))
        {
            characterConfig.EnableFlyingRouletteButton = enableFlyingRouletteButton;
            _ = _services.Framework.RunOnFrameworkThread(() => _services.GameFunctions.ToggleFlyingRouletteButton(enableFlyingRouletteButton));
        }

        if (ImGui.IsItemHovered())
        {
            _mainCommands ??= _services.DataManager.GetExcelSheet<MainCommand>()!
                .Where(x => x.RowId is 3 or 61).Select(x => (x.RowId, x.Name.ExtractText()))
                .ToArray();

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, ImGui.GetStyle().ItemSpacing.Y));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.BeginTooltip();

                ImGui.Text("「飛行坐騎隨機召喚」可從「"u8);
                ImGui.SameLine();
                for (int i = 0; i < _mainCommands.Length; i++)
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 1f, 1), StringCache.MainCommands[_mainCommands[i].RowId, () => _mainCommands[i].Name]);
                    ImGui.SameLine();

                    if (i < _mainCommands.Length - 1)
                    {
                        ImGui.Text("」與「"u8);
                        ImGui.SameLine();
                    }
                }

                ImGui.Text("」視窗使用"u8);

                ImGui.Separator();
                ImGui.TextColored(
                    new Vector4(1f, 0.75f, 0.2f, 1f),
                    "實驗性功能，預設關閉：此按鈕依賴尚未在台服驗證的介面掛鉤位址。");
                ImGui.TextColored(
                    new Vector4(1f, 0.75f, 0.2f, 1f),
                    "開啟後若坐騎導覽或輪盤按鈕行為異常，請關閉此選項並回報。");
                ImGui.EndTooltip();
            }

            ImGui.PopStyleVar();
        }
    }

    private void RouletteGroup(CharacterConfig characterConfig, ref string? groupName, ref bool show, bool isFlying = false)
    {
        ImGuiStylePtr style = ImGui.GetStyle();

        const int ROWS = 2;
        float spacing = style.ItemSpacing.Y * (ROWS - 1);
        float checkboxHeight = ImGui.GetFrameHeight();
        float contentHeight = spacing + (checkboxHeight * ROWS);
        float totalHeight = contentHeight + (style.FramePadding.Y * 2) + style.ItemSpacing.Y;

        if (ImGui.BeginChildFrame(isFlying ? 2u : 1u, new Vector2(0, totalHeight)))
        {
            if (ImGui.BeginTable(RouletteGroupID(isFlying), 2))
            {
                ImGui.TableSetupColumn("##icon"u8, ImGuiTableColumnFlags.WidthFixed, contentHeight);
                ImGui.TableSetupColumn("##settings"u8, ImGuiTableColumnFlags.WidthStretch);

                _ = ImGui.TableNextColumn();

                ImGui.Image(_services.TextureHelper.LoadIconTexture(isFlying ? 122u : 118u), new Vector2(contentHeight));

                _ = ImGui.TableNextColumn();

                SelectRouletteGroup(characterConfig, ref groupName, isFlying);

                _ = ImGui.Checkbox("在詠唱欄顯示坐騎"u8, ref show);

                ImGui.EndTable();
            }

            ImGui.EndChildFrame();
        }
    }

    private static ReadOnlySpan<byte> RouletteGroupID(bool isFlying)
    {
        return isFlying
            ? "##roulettegroup_f"u8
            : "##roulettegroup_g"u8;
    }

    private static void SelectRouletteGroup(CharacterConfig characterConfig, ref string? groupName, bool isFlying = false)
    {
        bool isEnabled = groupName is not null;

        _ = ImGui.Checkbox("改用坐騎群組"u8, ref isEnabled);

        if (isEnabled)
        {
            groupName ??= characterConfig.Groups.FirstOrDefault()?.Name;

            if (groupName is not null)
            {
                ImGui.SameLine();
                SelectMountGroup(characterConfig, ref groupName, isFlying);
            }
        }
        else
        {
            groupName = null;
        }

        static void SelectMountGroup(CharacterConfig config, ref string group, bool isFlying)
        {
            ControlHelper.SelectItem(
                config.Groups,
                x => x.Name,
                ref group,
                RouletteGroupID(isFlying),
                100);
        }
    }
}
