namespace BetterMountRoulette.UI;

using BetterMountRoulette.Config;
using BetterMountRoulette.Config.Data;
using BetterMountRoulette.Util;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;

using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class MountGroupPage
{
    private readonly BetterMountRoulettePlugin _plugin;
    private readonly MountRenderer _mountRenderer;
    private string? _currentMountGroup;
    private MountGroupPageEnum _mode = MountGroupPageEnum.Settings;

    // _rawNameFilter is the input buffer, _nameFilter the trimmed value actually used for
    // filtering. Keeping them separate is what lets the user type a trailing space.
    private string _rawNameFilter = "";
    private string _nameFilter = "";
    private List<MountData>? _filteredMounts;
    private (int UnlockedCount, string Text) _lastFilter;

    private enum MountGroupPageEnum
    {
        Settings,
        Mounts
    }

    internal MountGroupPage(BetterMountRoulettePlugin plugin, PluginServices services)
    {
        _plugin = plugin;
        _mountRenderer = new MountRenderer(services);
    }

    public void RenderPage(CharacterConfig characterConfig)
    {
        MountGroup mounts = SelectCurrentGroup(characterConfig);
        DrawMountGroup(mounts);
    }

    private void DrawMountGroup(MountGroup group)
    {
        if (group is null)
        {
            ImGui.Text("找不到坐騎群組！"u8);
            return;
        }

        bool isSettingsOpen = _mode == MountGroupPageEnum.Settings;
        bool isMountsOpen = _mode == MountGroupPageEnum.Mounts;
        bool enableNewMounts = !group.IncludedMeansActive;

        ImGui.GetStateStorage().SetInt(ImGui.GetID("Settings"u8), isSettingsOpen ? 1 : 0);
        ImGui.BeginDisabled(isSettingsOpen);
        if (ImGui.CollapsingHeader("設定##Settings"u8))
        {
            ImGui.EndDisabled();
            isSettingsOpen = true;
            RenderGroupSettings(group, ref enableNewMounts);
        }
        else
        {
            ImGui.EndDisabled();
        }

        List<MountData> unlockedMounts = _plugin.MountRegistry.GetUnlockedMounts();
        UpdateMountSelectionData(group, unlockedMounts, enableNewMounts);

        ImGui.GetStateStorage().SetInt(ImGui.GetID("Mounts"u8), isMountsOpen ? 1 : 0);
        ImGui.BeginDisabled(isMountsOpen);
        if (ImGui.CollapsingHeader("坐騎##Mounts"u8))
        {
            ImGui.EndDisabled();
            isMountsOpen = true;

            if (unlockedMounts.Count > 0)
            {
                DrawNameFilter();
            }

            List<MountData> filteredAndUnlockedMounts = ApplyFilterAndGetFilteredMounts(unlockedMounts);

            int pages = MountRenderer.GetPageCount(filteredAndUnlockedMounts.Count);
            if (pages == 0)
            {
                ImGui.Text(
                    unlockedMounts.Count == 0
                        ? "請至少解鎖一隻坐騎。"u8
                        : "沒有符合篩選條件的坐騎。"u8);
            }
            else if (ImGui.BeginTabBar("mount_pages"u8))
            {
                for (int page = 1; page <= pages; page++)
                {
                    if (ImGui.BeginTabItem(StringCache.Pages[page , () => $"{page}" ]))
                    {
                        RenderMountListPage(page, group, filteredAndUnlockedMounts);
                        ImGui.EndTabItem();
                    }
                }

                ImGui.SameLine();
                ImGui.EndTabBar();
            }
        }
        else
        {
            ImGui.EndDisabled();
        }

        switch (_mode)
        {
            case MountGroupPageEnum.Settings when isMountsOpen:
                _mode = MountGroupPageEnum.Mounts;
                break;
            case MountGroupPageEnum.Mounts when isSettingsOpen:
                _mode = MountGroupPageEnum.Settings;
                break;
            case MountGroupPageEnum.Settings:
            case MountGroupPageEnum.Mounts:
                break;
            default:
                // Something somewhere went horribly wrong. Reset to settings.
                _mode = MountGroupPageEnum.Settings;
                break;
        }
    }

    private void DrawNameFilter()
    {
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 250);
        if (ImGui.InputTextWithHint("###nameFilter"u8, "搜尋名稱…"u8, ref _rawNameFilter, 100))
        {
            _nameFilter = _rawNameFilter.Trim();
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.FilterCircleXmark))
        {
            _rawNameFilter = "";
            _nameFilter = "";
        }

        ControlHelper.Tooltip("清除名稱篩選"u8);
    }

    private List<MountData> ApplyFilterAndGetFilteredMounts(List<MountData> unlockedMounts)
    {
        if (string.IsNullOrEmpty(_nameFilter))
        {
            return unlockedMounts;
        }

        if (_filteredMounts is null
            || unlockedMounts.Count != _lastFilter.UnlockedCount
            || !_nameFilter.Equals(_lastFilter.Text, StringComparison.OrdinalIgnoreCase))
        {
            _filteredMounts = unlockedMounts
                .Where(mountData => mountData.Name.ExtractText().Contains(_nameFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        _lastFilter = (UnlockedCount: unlockedMounts.Count, Text: _nameFilter);

        return _filteredMounts;
    }

    private void RenderMountListPage(int page, MountGroup group, List<MountData> unlockedAndFilteredMounts)
    {
        _mountRenderer.RenderPage(unlockedAndFilteredMounts, group, page);

        (bool Select, int? Page)? maybeInfo = null;

        Button("全部選取"u8, ref maybeInfo, (true, null));
        ImGui.SameLine();
        Button("全部取消選取"u8, ref maybeInfo, (false, null));
        ImGui.SameLine();
        Button("選取此頁"u8, ref maybeInfo, (true, page));
        ImGui.SameLine();
        Button("取消選取此頁"u8, ref maybeInfo, (false, page));

        if (maybeInfo is { } info)
        {
            string selectText = info.Select ? "選取" : "取消選取";
            string pageInfo = (info.Page, info.Select) switch
            {
                (null, true) => string.IsNullOrEmpty(_nameFilter)
                    ? "目前未選取的坐騎"
                    : $"符合「{_nameFilter}」的坐騎",
                (null, false) => string.IsNullOrEmpty(_nameFilter)
                    ? "目前已選取的坐騎"
                    : $"符合「{_nameFilter}」的坐騎",
                _ => "目前頁面上的坐騎",
            };

            _plugin.WindowManager.ConfirmYesNo(
                "確定嗎？",
                $"確定要{selectText}所有{pageInfo}嗎？",
                () => MountRenderer.Update(
                    unlockedAndFilteredMounts,
                    group,
                    info.Select,
                    info.Page));
        }

        static void Button(ReadOnlySpan<byte> label, ref (bool, int?)? maybeInfo, (bool, int?) value)
        {
            if (ImGui.Button(label))
            {
                maybeInfo = value;
            }
        }
    }

    private void RenderGroupSettings(MountGroup group, ref bool enableNewMounts)
    {
        bool forceMultiseatersInParty = group.ForceMultiseatersInParty;
        bool preferMoreSeats = group.PreferMoreSeats;
        bool forceSingleSeatersWhileSolo = group.ForceSingleSeatersWhileSolo;
        bool pvpOverride = group.PvpOverrideMultiseaterSettings;
        bool pvpForceMultiseatersInParty = group.PvpForceMultiseatersInParty;
        bool pvpPreferMoreSeats = group.PvpPreferMoreSeats;
        bool pvpForceSingleSeatersWhileSolo = group.PvpForceSingleSeatersWhileSolo;
        bool fastMode = group.FastMode != FastMode.Off;
        bool fastModeAlways = group.FastMode == FastMode.On;
        RouletteDisplayType displayType = group.DisplayType;

        _ = ImGui.Checkbox("解鎖新坐騎時自動啟用", ref enableNewMounts);

        _ = ImGui.Checkbox("組隊時只使用多人坐騎"u8, ref forceMultiseatersInParty);
        ControlHelper.Tooltip("跨界隊伍無法共乘，因此此選項不會生效。"u8);

        ImGui.Indent();
        ImGui.BeginDisabled(!forceMultiseatersInParty);
        _ = ImGui.Checkbox("優先使用可搭載較多隊員的坐騎"u8, ref preferMoreSeats);
        ImGui.EndDisabled();
        ImGui.Unindent();

        ControlHelper.Tooltip("隨機坐騎必須能容納目前隊伍中盡可能多的成員。"u8);

        _ = ImGui.Checkbox("單人時只使用單人坐騎"u8, ref forceSingleSeatersWhileSolo);
        ControlHelper.Tooltip("在跨界隊伍中也會套用。"u8);

        _ = ImGui.Checkbox("PvP（紛爭前線與烈羽爭鋒）使用不同設定"u8, ref pvpOverride);
        ImGui.Indent();
        ImGui.BeginDisabled(!pvpOverride);
        ImGui.PushID(1);
        _ = ImGui.Checkbox("組隊時只使用多人坐騎"u8, ref pvpForceMultiseatersInParty);

        ImGui.Indent();
        ImGui.BeginDisabled(!pvpForceMultiseatersInParty);
        _ = ImGui.Checkbox("優先使用可搭載較多隊員的坐騎"u8, ref pvpPreferMoreSeats);
        ImGui.EndDisabled();
        ImGui.Unindent();

        _ = ImGui.Checkbox("單人時只使用單人坐騎"u8, ref pvpForceSingleSeatersWhileSolo);

        ImGui.PopID();
        ImGui.EndDisabled();
        ImGui.Unindent();

        _ = ImGui.Checkbox("使用地面速度最快的坐騎"u8, ref fastMode);

        if (ControlHelper.BeginTooltip())
        {
            string GetFastMountsText()
            {
                return $"在可提高坐騎速度的區域，將坐騎限制為 {string.Join("/", _plugin.MountRegistry.GetFastMountNames())}，";
            }

            ImGui.Text(StringCache.Named["FastMountsText", GetFastMountsText]);
            ImGui.Text("除非已解鎖至少第一階段的坐騎速度強化或飛行。"u8);
            ImGui.Text("至少要解鎖並啟用其中一隻坐騎才會生效。"u8);
            ImGui.EndTooltip();
        }

        ImGui.Indent();
        ImGui.BeginDisabled(!fastMode);
        _ = ImGui.Checkbox("即使已解鎖飛行，仍一律使用地面速度最快的坐騎"u8, ref fastModeAlways);
        ImGui.EndDisabled();
        ImGui.Unindent();

        ControlHelper.Tooltip("不論是否解鎖飛行，皆限制坐騎選擇。"u8);

        ImGui.AlignTextToFramePadding();
        ImGui.Text("/pmount 顯示方式："u8);
        ImGui.SameLine();
        SelectDisplayType(ref displayType);

        group.DisplayType = displayType;
        group.ForceMultiseatersInParty = forceMultiseatersInParty;
        group.PreferMoreSeats = preferMoreSeats;
        group.ForceSingleSeatersWhileSolo = forceSingleSeatersWhileSolo;
        group.PvpOverrideMultiseaterSettings = pvpOverride;
        group.PvpForceMultiseatersInParty = pvpForceMultiseatersInParty;
        group.PvpPreferMoreSeats = pvpPreferMoreSeats;
        group.PvpForceSingleSeatersWhileSolo = pvpForceSingleSeatersWhileSolo;
        group.FastMode = fastModeAlways ? FastMode.On : fastMode ? FastMode.IfGrounded : FastMode.Off;
    }

    private static void SelectDisplayType(ref RouletteDisplayType displayType)
    {
        if (ImGui.BeginCombo("##displayType"u8, DisplayTypeValue(displayType)))
        {
            ComboItem(RouletteDisplayType.Grounded, ref displayType);
            ComboItem(RouletteDisplayType.Flying, ref displayType);
            ComboItem(RouletteDisplayType.Show, ref displayType);

            ImGui.EndCombo();
        }

        static void ComboItem(RouletteDisplayType value, ref RouletteDisplayType selectedValue)
        {
            if (ImGui.Selectable(DisplayTypeValue(value), value == selectedValue))
            {
                selectedValue = value;
            }
        }

        static ReadOnlySpan<byte> DisplayTypeValue(RouletteDisplayType displayType)
        {
            return displayType switch
            {
                RouletteDisplayType.Grounded => "顯示為坐騎隨機召喚"u8,
                RouletteDisplayType.Flying => "顯示為飛行坐騎隨機召喚"u8,
                RouletteDisplayType.Show => "詠唱期間顯示坐騎"u8,
                _ => StringCache.Named[$"RouletteDisplayType_{displayType}", displayType.ToString],
            };
        }
    }

    private static void UpdateMountSelectionData(MountGroup group, List<MountData> unlockedMounts, bool enableNewMounts)
    {
        if (enableNewMounts == group.IncludedMeansActive)
        {
            // we auto-enable new mounts by tracking which mounts are explicitly disabled
            group.IncludedMeansActive = !enableNewMounts;

            // invert selection
            var unlockedMountIDs = unlockedMounts.Select(x => x.ID).ToHashSet();
            unlockedMountIDs.ExceptWith(group.IncludedMounts);
            group.IncludedMounts.Clear();
            group.IncludedMounts.UnionWith(unlockedMountIDs);
        }
    }

    private MountGroup SelectCurrentGroup(CharacterConfig characterConfig)
    {
        if (_currentMountGroup is not null && characterConfig.Groups.All(x => x.Name != _currentMountGroup))
        {
            _currentMountGroup = null;
        }

        _currentMountGroup ??= characterConfig.Groups.First().Name;

        ControlHelper.SelectItem(characterConfig.Groups, x => x.Name, ref _currentMountGroup, "##currentgroup"u8, 150);

        string currentGroup = _currentMountGroup;
        ImGui.SameLine();
        if (ImGui.Button("新增"u8))
        {
            var dialog = new RenameItemDialog(
                "新增群組",
                string.Empty,
                x => AddMountGroup(characterConfig, x))
            {
                NormalizeWhitespace = true
            };

            dialog.SetValidation(x => ValidateGroup(x, isNew: true), x => "已有同名群組。"u8);
            _plugin.WindowManager.OpenDialog(dialog);
        }

        ImGui.SameLine();
        if (ImGui.Button("編輯"))
        {
            var dialog = new RenameItemDialog(
                $"重新命名 {_currentMountGroup}",
                _currentMountGroup,
                (newName) => RenameMountGroup(_currentMountGroup, newName))
            {
                NormalizeWhitespace = true
            };

            dialog.SetValidation(x => ValidateGroup(x, isNew: false), x => "已有其他同名群組。"u8);

            _plugin.WindowManager.OpenDialog(dialog);
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(!characterConfig.HasNonDefaultGroups);
        if (ImGui.Button("刪除"))
        {
            _plugin.WindowManager.Confirm(
                "確認刪除坐騎群組",
                $"確定要刪除 {currentGroup} 嗎？\n此操作無法復原。",
                ("確定", () => DeleteMountGroup(currentGroup)),
                "取消");
        }

        ImGui.EndDisabled();

        return characterConfig.GetMountGroup(_currentMountGroup)!;

        bool ValidateGroup(string newName, bool isNew)
        {
            if (_plugin.CharacterConfig is not { } characterConfig)
            {
                return false;
            }

            HashSet<string> names = new(characterConfig.Groups.Select(x => x.Name), StringComparer.InvariantCultureIgnoreCase);

            if (!isNew)
            {
                _ = names.Remove(currentGroup);
            }

            return !names.Contains(newName);
        }
    }

    private void DeleteMountGroup(string name)
    {
        if (_plugin.CharacterConfig is not { } characterConfig)
        {
            return;
        }

        MountGroupManager.Delete(characterConfig, name);

        if (_currentMountGroup == name)
        {
            _currentMountGroup = null;
        }
    }

    private void RenameMountGroup(string currentMountGroup, string newName)
    {
        if (_plugin.CharacterConfig is not { } characterConfig)
        {
            return;
        }

        MountGroupManager.Rename(characterConfig, currentMountGroup, newName);

        if (_currentMountGroup == currentMountGroup)
        {
            _currentMountGroup = newName;
        }
    }

    private void AddMountGroup(CharacterConfig characterConfig, string name)
    {
        characterConfig.Groups.Add(new MountGroup { Name = name });
        _currentMountGroup = name;
    }
}
