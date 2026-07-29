namespace BetterMountRoulette.UI;

using BetterMountRoulette.Config;
using BetterMountRoulette.Config.Data;
using BetterMountRoulette.Util;

using Dalamud.Bindings.ImGui;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

internal sealed class CharacterManagementRenderer(
    PluginServices services,
    WindowManager windowManager,
    CharacterManager characterManager,
    Configuration configuration)
{
    private readonly PluginServices _services = services;
    private readonly WindowManager _windowManager = windowManager;
    private readonly CharacterManager _characterManager = characterManager;
    private readonly Configuration _configuration = configuration;
    private ulong? _currentCharacter;

    public void Draw()
    {
        RenderNewCharacterHandling();

        ImGui.Text("現有角色"u8);
        if (!ImGui.BeginListBox("##Characters"u8))
        {
            return;
        }

        ReadOnlySpan<byte> selectedCharacterName = null;
        foreach (KeyValuePair<ulong, CharacterConfigEntry> character in _configuration.CharacterConfigs.OrderBy(x => x.Key))
        {
            ReadOnlySpan<byte> text = StringCache.Characters[character.Key, () => FormatCharacter(character.Value)];

            if (ImGui.Selectable(text, _currentCharacter == character.Key))
            {
                Util.Toggle(ref _currentCharacter, character.Key);
            }

            if (_currentCharacter == character.Key)
            {
                selectedCharacterName = text;
            }
        }

        ImGui.EndListBox();
        ImGui.BeginDisabled(_currentCharacter is null || _currentCharacter == _services.ClientState.LocalContentId);

        if (ImGui.Button("匯入"))
        {
            Debug.Assert(_currentCharacter is not null);
            ulong currentCharacter = _currentCharacter.Value;
            _windowManager.Confirm(
                "匯入設定？",
                $"要從 {Encoding.UTF8.GetString(selectedCharacterName)} 匯入設定嗎？這會覆寫此角色的所有設定！",
                ("確認", () => ImportFromCharacter(currentCharacter)),
                "取消");
        }

        ImGui.SameLine();

        ImGui.BeginDisabled(_currentCharacter == Configuration.DUMMY_LEGACY_CONFIG_ID);
        if (ImGui.Button("刪除"))
        {
            Debug.Assert(_currentCharacter is not null);
            ulong currentCharacter = _currentCharacter.Value;
            _windowManager.Confirm(
                "刪除設定？",
                $"要刪除 {Encoding.UTF8.GetString(selectedCharacterName)} 的設定嗎？此操作無法復原！",
                ("確認", () => DeleteCharacter(currentCharacter)),
                "取消");
        }

        if (_currentCharacter == Configuration.DUMMY_LEGACY_CONFIG_ID)
        {
            ImGui.SameLine();
            ImGui.Text("此設定無法刪除。"u8);
        }
        else if (_currentCharacter == _services.ClientState.LocalContentId)
        {
            ImGui.SameLine();
            ImGui.Text("無法從目前使用中的角色匯入或刪除設定。"u8);
        }

        ImGui.EndDisabled();
        ImGui.EndDisabled();
    }

    private void RenderNewCharacterHandling()
    {
        const int ASK = Configuration.NewCharacterHandlingModes.ASK;
        const int BLANK = Configuration.NewCharacterHandlingModes.BLANK;
        const int IMPORT = Configuration.NewCharacterHandlingModes.IMPORT;

        if (_configuration.CharacterConfigs.ContainsKey(Configuration.DUMMY_LEGACY_CONFIG_ID))
        {
            ReadOnlySpan<byte> text = "新角色："u8;
            Vector2 offset = ImGui.CalcTextSize(text);
            float posX = ImGui.GetCursorPosX();
            ImGui.SetCursorPosX(posX + offset.X);

            ReadOnlySpan<byte> characterHandlingMode = GetCharacterHandlingModeText(_configuration.NewCharacterHandling);

            if (ImGui.BeginCombo("##NewCharacterHandling"u8, characterHandlingMode))
            {
                int? newCharacterHandling = _configuration.NewCharacterHandling;
                DrawSelection(ASK, ref newCharacterHandling);
                DrawSelection(BLANK, ref newCharacterHandling);
                DrawSelection(IMPORT, ref newCharacterHandling);
                _configuration.NewCharacterHandling = newCharacterHandling;

                ImGui.EndCombo();
            }

            ImGui.SameLine();
            ImGui.SetCursorPosX(posX);
            ImGui.Text(text);

            ImGui.Text("模式：");
            ImGui.Text(StringCache.Named["NewCharacterImport", () => CharacterHandlingModeExplanation(IMPORT)]);
            ImGui.Text(StringCache.Named["NewCharacterBlank", () => CharacterHandlingModeExplanation(BLANK)]);
            ImGui.Text(StringCache.Named["NewCharacterAsk", () => CharacterHandlingModeExplanation(ASK)]);
            ImGui.Separator();
        }

        static void DrawSelection(int mode, ref int? selectedMode)
        {
            if (ImGui.Selectable(GetCharacterHandlingModeText(mode), mode == selectedMode))
            {
                selectedMode = mode;
            }
        }

        static byte[] Concat(ReadOnlySpan<byte> part1, ReadOnlySpan<byte> part2, ReadOnlySpan<byte> part3)
        {
            byte[] res = new byte[part1.Length + part2.Length + part3.Length];
            part1.CopyTo(res.AsSpan());
            part2.CopyTo(res.AsSpan(part1.Length));
            part3.CopyTo(res.AsSpan(part1.Length + part2.Length));
            return res;
        }

        static byte[] CharacterHandlingModeExplanation(int? characterHandlingMode)
        {
            ReadOnlySpan<byte> part1 = "• "u8;
            ReadOnlySpan<byte> part2 = GetCharacterHandlingModeText(characterHandlingMode);
            ReadOnlySpan<byte> part3 = characterHandlingMode switch
            {
                BLANK => "：為新角色建立空白設定檔。"u8,
                IMPORT => "：新角色首次登入時匯入舊版資料。"u8,
                ASK or _ => "：針對每個角色個別詢問是否匯入。"u8,
            };

            return Concat(part1, part2, part3);
        } 

        static ReadOnlySpan<byte> GetCharacterHandlingModeText(int? characterHandlingMode)
        {
            return characterHandlingMode switch
            {
                ASK => "詢問"u8,
                BLANK => "建立空白設定檔"u8,
                IMPORT => "匯入舊版資料"u8,
                _ => "詢問"u8,
            };
        }
    }

    private static string FormatCharacter(CharacterConfigEntry entry)
    {
        StringBuilder sb = new(entry.CharacterName);
        if (!string.IsNullOrWhiteSpace(entry.CharacterWorld))
        {
            _ = sb.Append(CultureInfo.CurrentCulture, $" ({entry.CharacterWorld})");
        }

        return sb.ToString();
    }

    private void ImportFromCharacter(ulong characterID)
    {
        if (_characterManager.Import(characterID))
        {
            _windowManager.Confirm("匯入", "匯入成功！", "確定");
        }
        else
        {
            _windowManager.Confirm("匯入", "匯入失敗：無法存取角色設定。", "確定");
        }
    }

    private void DeleteCharacter(ulong characterID)
    {
        if (_configuration.CharacterConfigs.TryGetValue(characterID, out CharacterConfigEntry? cce))
        {
            _ = _configuration.CharacterConfigs.Remove(characterID);
            if (cce is not null && characterID is not Configuration.DUMMY_LEGACY_CONFIG_ID)
            {
                try
                {
                    File.Delete(Path.Combine(_services.DalamudPluginInterface.GetPluginConfigDirectory(), cce.FileName));
                }
                catch (IOException)
                {
                }
            }

            _services.DalamudPluginInterface.SavePluginConfig(_configuration);
        }
    }
}
