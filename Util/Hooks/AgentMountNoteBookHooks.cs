namespace BetterMountRoulette.Util.Hooks;
using Dalamud.Hooking;

using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

using System;
using System.Runtime.InteropServices;

internal sealed class AgentMountNoteBookHooks : IDisposable
{
    private bool _disposedValue;
    private readonly PluginServices _services;
    private readonly nint _agentAddress;
    private readonly nint _vtableAddress;

    // Contract: either all four hooks exist (_installed) or none does. Enable/Disable are no-ops when not installed;
    // the detours only ever run through an installed hook.
    private readonly bool _installed;
    private readonly Hook<AgentMountNoteBookUseRouletteDetour>? _agentMountNoteBookUseRouletteHook;
    private readonly Hook<AgentMountNoteBookGetRouletteIconDetour>? _agentMountNoteBookGetRouletteIconHook;
    private readonly Hook<AgentMountNoteBookGetRouletteActionIdDetour>? _agentMountNoteBookGetRouletteActionIdHook;
    private readonly Hook<AgentMountNoteBookIsRouletteAvailableDetour>? _agentMountNoteBookIsRouletteAvailableHook;

    private unsafe delegate bool AgentMountNoteBookUseRouletteDetour(AgentInterface* @this, uint rouletteIndex);
    private unsafe delegate uint AgentMountNoteBookGetRouletteIconDetour(AgentInterface* @this, uint rouletteIndex);
    private unsafe delegate uint AgentMountNoteBookGetRouletteActionIdDetour(AgentInterface* @this, uint rouletteIndex);
    private unsafe delegate bool AgentMountNoteBookIsRouletteAvailableDetour(AgentInterface* @this, uint rouletteIndex);

    public unsafe AgentMountNoteBookHooks(PluginServices services)
    {
        _services = services;

        // AgentModule.Instance() and the MountNotebook agent can both legitimately be null before the UI module
        // has finished initializing. Dereferencing either would be an uncatchable AccessViolationException that
        // crashes the whole game, so leave the hooks uninstalled instead (the plugin still loads; only the
        // Mount Guide button is lost).
        AgentModule* agentModule = AgentModule.Instance();
        AgentInterface* agent = agentModule == null ? null : agentModule->GetAgentByInternalId(AgentId.MountNotebook);
        if (agent == null || agent->VirtualTable == null)
        {
            services.PluginLog.Warning(
                "[改版防護] 取不到坐騎圖鑑的介面物件：坐騎圖鑑裡的飛行坐騎隨機按鈕停用（重新載入插件前都不會出現），其餘功能不受影響。");
            return;
        }

        var vtable = (AgentMountNoteBookVTable*)agent->VirtualTable;
        _agentAddress = (nint)agent;
        _vtableAddress = (nint)vtable;
        if (vtable->UseRoulette == null
            || vtable->GetRouletteIcon == null
            || vtable->GetRouletteActionId == null
            || vtable->IsRouletteAvailable == null)
        {
            services.PluginLog.Warning(
                "[改版防護] 坐騎圖鑑的虛擬函式表有空的欄位：坐騎圖鑑裡的飛行坐騎隨機按鈕停用，其餘功能不受影響。");
            return;
        }

        _agentMountNoteBookUseRouletteHook = services.GameInteropProvider.HookFromAddress<AgentMountNoteBookUseRouletteDetour>(
            vtable->UseRoulette,
            OnUseRoulette);
        _agentMountNoteBookGetRouletteIconHook = services.GameInteropProvider.HookFromAddress<AgentMountNoteBookGetRouletteIconDetour>(
            vtable->GetRouletteIcon,
            OnGetRouletteIcon);
        _agentMountNoteBookGetRouletteActionIdHook = services.GameInteropProvider.HookFromAddress<AgentMountNoteBookGetRouletteActionIdDetour>(
            vtable->GetRouletteActionId,
            OnGetRouletteActionId);
        _agentMountNoteBookIsRouletteAvailableHook = services.GameInteropProvider.HookFromAddress<AgentMountNoteBookIsRouletteAvailableDetour>(
            vtable->IsRouletteAvailable,
            OnIsRouletteAvailable);
        _installed = true;
    }

    internal void Enable()
    {
        if (!_installed)
        {
            return;
        }

        _agentMountNoteBookUseRouletteHook!.Enable();
        _agentMountNoteBookGetRouletteIconHook!.Enable();
        _agentMountNoteBookGetRouletteActionIdHook!.Enable();
        _agentMountNoteBookIsRouletteAvailableHook!.Enable();

        // Information level (a diagnostic the user is asked to report) so the hardcoded vtable offsets can be verified on the live
        // TC client: if an offset resolved to the wrong function, these addresses will not line up with the real
        // MountNotebook agent vtable and the roulette-button behaviour will visibly misbehave.
        _services.PluginLog.Information(
            $"[飛行輪盤鈕] hook 掛載 agent=0x{_agentAddress:X} vtable=0x{_vtableAddress:X} " +
            $"UseRoulette=0x{_agentMountNoteBookUseRouletteHook!.Address:X} " +
            $"IsRouletteAvailable=0x{_agentMountNoteBookIsRouletteAvailableHook!.Address:X} " +
            $"GetRouletteActionId=0x{_agentMountNoteBookGetRouletteActionIdHook!.Address:X} " +
            $"GetRouletteIcon=0x{_agentMountNoteBookGetRouletteIconHook!.Address:X}");
    }

    internal void Disable()
    {
        if (!_installed)
        {
            return;
        }

        _agentMountNoteBookUseRouletteHook!.Disable();
        _agentMountNoteBookGetRouletteIconHook!.Disable();
        _agentMountNoteBookGetRouletteActionIdHook!.Disable();
        _agentMountNoteBookIsRouletteAvailableHook!.Disable();
    }

    private unsafe bool OnIsRouletteAvailable(AgentInterface* @this, uint rouletteIndex)
    {
        _services.PluginLog.Debug($"OnIsRouletteAvailable(this, {rouletteIndex})");
        if (rouletteIndex == 1)
        {
            rouletteIndex = 0;
        }

        return _agentMountNoteBookIsRouletteAvailableHook!.Original(@this, rouletteIndex);
    }

    private unsafe uint OnGetRouletteActionId(AgentInterface* @this, uint rouletteIndex)
    {
        _services.PluginLog.Debug($"OnGetRouletteActionId(this, {rouletteIndex})");
        return rouletteIndex == 1
            ? 24
            : _agentMountNoteBookGetRouletteActionIdHook!.Original(@this, rouletteIndex);
    }

    private unsafe uint OnGetRouletteIcon(AgentInterface* @this, uint rouletteIndex)
    {
        _services.PluginLog.Debug($"OnGetRouletteIcon(this, {rouletteIndex})");
        return rouletteIndex == 1
            ? 122
            : _agentMountNoteBookGetRouletteIconHook!.Original(@this, rouletteIndex);
    }

    private unsafe bool OnUseRoulette(AgentInterface* @this, uint rouletteIndex)
    {
        _services.PluginLog.Debug($"OnUseRoulette(this, {rouletteIndex})");
        return rouletteIndex == 1
            ? ActionManager.Instance()->UseAction(ActionType.GeneralAction, 24)
            : _agentMountNoteBookUseRouletteHook!.Original(@this, rouletteIndex);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            _agentMountNoteBookUseRouletteHook?.Dispose();
            _agentMountNoteBookGetRouletteIconHook?.Dispose();
            _agentMountNoteBookGetRouletteActionIdHook?.Dispose();
            _agentMountNoteBookIsRouletteAvailableHook?.Dispose();

            _disposedValue = true;
        }
    }

    ~AgentMountNoteBookHooks()
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

    [StructLayout(LayoutKind.Explicit, Size = 0x160)]
    private unsafe struct AgentMountNoteBookVTable
    {
        [FieldOffset(0x90)]
        public unsafe delegate* unmanaged<AgentInterface*, uint, bool> UseRoulette;

        [FieldOffset(0xC0)]
        public unsafe delegate* unmanaged<AgentInterface*, uint, bool> IsRouletteAvailable;

        [FieldOffset(0xC8)]
        public unsafe delegate* unmanaged<AgentInterface*, uint, uint> GetRouletteActionId;

        [FieldOffset(0xD0)]
        public unsafe delegate* unmanaged<AgentInterface*, uint, uint> GetRouletteIcon;
    }
}
