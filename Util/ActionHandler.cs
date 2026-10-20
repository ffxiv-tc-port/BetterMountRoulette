namespace BetterMountRoulette.Util;

using BetterMountRoulette.Config.Data;
using BetterMountRoulette.UI;

using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;

using FFXIVClientStructs.FFXIV.Client.Game;

using System;
using System.Diagnostics.CodeAnalysis;

internal sealed class ActionHandler : IDisposable
{
    private const uint NORMAL_ROULETTE_ACTION_ID = 9;
    private const uint FLYING_ROULETTE_ACTION_ID = 24;

    private readonly PluginServices _services;
    private readonly MountRegistry _mountRegistry;
    private readonly Hook<UseActionHandler>? _useActionHook;
    private readonly GameFunctions _gameFunctions;
    private RouletteDisplayType? _displayTypeOverride;
    private bool _disposedValue;

    public unsafe ActionHandler(
        PluginServices services,
        MountRegistry mountRegistry)
    {
        _services = services;
        _mountRegistry = mountRegistry;
        _gameFunctions = _services.GameFunctions;
        void* renderAddress = ActionManager.MemberFunctionPointers.UseAction;
        if (renderAddress is null)
        {
            _services.PluginLog.Warning(
                "[改版防護] 取不到「使用技能」的遊戲函式位址：坐騎隨機不會改成從自訂群組挑選"
                + "（遊戲原本的坐騎隨機照常），/pmount 也無法召喚。");
            return;
        }

        _useActionHook = _services.GameInteropProvider.HookFromAddress<UseActionHandler>(renderAddress, OnUseAction);
        _useActionHook.Enable();
    }

    public unsafe delegate byte UseActionHandler(ActionManager* actionManager, ActionType actionType, uint actionID, long targetID = 3758096384U, uint a4 = 0U, uint a5 = 0U, uint a6 = 0U, void* a7 = default);

    // 遊戲原函式在這裡呼叫恰好一次。挑坐騎途中擲出例外就照玩家原本按的技能交給遊戲；例外不可穿回原生框架（＝遊戲崩潰）。
    [SuppressMessage("Design", "CA1031", Justification = "Hook detour: no exception may propagate into native code")]
    private unsafe byte OnUseAction(ActionManager* actionManager, ActionType actionType, uint actionID, long targetID, uint a4, uint a5, uint a6, void* a7)
    {
        ActionType requestedType = actionType;
        uint requestedId = actionID;
        try
        {
            RedirectRoulette(ref actionType, ref actionID);
        }
        catch (Exception ex)
        {
            actionType = requestedType;
            actionID = requestedId;
            DetourGuard.ReportOnce(
                _services,
                ref _redirectFailureReported,
                "[改版防護] 坐騎隨機改從自訂群組挑選時擲出例外，已攔下並照原本按的技能交給遊戲。",
                "坐騎隨機暫時不從自訂群組挑選，改用遊戲原本的坐騎隨機。",
                ex);
        }

        return _useActionHook!.Original(actionManager, actionType, actionID, targetID, a4, a5, a6, a7);
    }

    private int _redirectFailureReported;

    private unsafe void RedirectRoulette(ref ActionType actionType, ref uint actionID)
    {
        RouletteDisplayType? displayTypeOverride = _displayTypeOverride;
        _displayTypeOverride = null;

        if (_services.Condition[ConditionFlag.Mounted]
            || _services.Condition[ConditionFlag.RidingPillion]
            || CharacterConfig is null)
        {
            return;
        }

        (string? groupName, _) = (actionID, actionType) switch
        {
            (NORMAL_ROULETTE_ACTION_ID, ActionType.GeneralAction) => (CharacterConfig.MountRouletteGroup, true),
            (FLYING_ROULETTE_ACTION_ID, ActionType.GeneralAction) => (CharacterConfig.FlyingMountRouletteGroup, true),
            _ => (null, false),
        };

        ActionType oldActionType = actionType;
        uint oldActionId = actionID;
        if (groupName is not null)
        {
            MountGroup? mountGroup = CharacterConfig.GetMountGroup(groupName);

            uint newActionID = 0;
            if (mountGroup is not null)
            {
                newActionID = _mountRegistry.GetRandom(ActionManager.Instance(), mountGroup);
            }

            if (newActionID is not 0)
            {
                actionType = ActionType.Mount;
                actionID = newActionID;
            }
        }

        if (displayTypeOverride is { } displayType)
        {
            switch (displayType)
            {
                case RouletteDisplayType.Grounded:
                    _gameFunctions.NextMountRouletteOverride = MountRouletteOverride.NormalRoulette;
                    break;
                case RouletteDisplayType.Flying:
                    _gameFunctions.NextMountRouletteOverride = MountRouletteOverride.FlyingRoulette;
                    break;
                case RouletteDisplayType.Show:
                default:
                    // no-op
                    break;
            }
        }
        else if (oldActionType == ActionType.GeneralAction)
        {
            switch (oldActionId)
            {
                case FLYING_ROULETTE_ACTION_ID when CharacterConfig.RevealMountsFlying:
                case NORMAL_ROULETTE_ACTION_ID when CharacterConfig.RevealMountsNormal:
                    _gameFunctions.NextMountRouletteOverride = MountRouletteOverride.PlainMount;
                    break;
                case FLYING_ROULETTE_ACTION_ID when actionType != oldActionType:
                    _gameFunctions.NextMountRouletteOverride = MountRouletteOverride.FlyingRoulette;
                    break;
                case NORMAL_ROULETTE_ACTION_ID when actionType != oldActionType:
                    _gameFunctions.NextMountRouletteOverride = MountRouletteOverride.NormalRoulette;
                    break;
                default:
                    // no-op
                    break;
            }
        }
    }

    public CharacterConfig? CharacterConfig { get; set; }

    public unsafe void HandleMountCommand(string command, string arguments)
    {
        if (CharacterConfig is not { } characterConfig)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(arguments))
        {
            PrintError("請指定坐騎群組");
            return;
        }

        arguments = RenameItemDialog.NormalizeWhiteSpace(arguments);

        MountGroup? mountGroup = characterConfig.GetMountGroup(arguments);
        if (mountGroup == null)
        {
            // handle quotes because not doing that in the first place was a dumb decision
            if (arguments.StartsWith('"') && arguments.EndsWith('"'))
            {
                arguments = arguments[1..^1];
            }

            mountGroup = characterConfig.GetMountGroup(arguments);
            if (mountGroup == null)
            {
            PrintError($"找不到坐騎群組「{arguments}」。");
                return;
            }
        }

        if (ActionManager.MemberFunctionPointers.UseAction is null)
        {
            PrintError("遊戲改版後找不到「使用技能」的遊戲函式，暫時無法用 /pmount 召喚坐騎。");
            return;
        }

        uint mount = _mountRegistry.GetRandom(ActionManager.Instance(), mountGroup);
        if (mount is not 0)
        {
            _displayTypeOverride = mountGroup.DisplayType;
            _ = ActionManager.Instance()->UseAction(ActionType.Mount, mount);
        }
        else
        {
            PrintError($"無法從群組「{arguments}」召喚坐騎。");
        }
    }

    internal void PrintError(string message)
    {
        _services.PluginLog.Error(message);
        if (!(CharacterConfig?.SuppressChatErrors ?? false))
        {
            _services.Chat.PrintError(message);
        }
    }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            _useActionHook?.Dispose();

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    ~ActionHandler()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
