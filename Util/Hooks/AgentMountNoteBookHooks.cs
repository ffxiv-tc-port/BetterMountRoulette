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
    private readonly Hook<AgentMountNoteBookUseRouletteDetour> _agentMountNoteBookUseRouletteHook;
    private readonly Hook<AgentMountNoteBookGetRouletteIconDetour> _agentMountNoteBookGetRouletteIconHook;
    private readonly Hook<AgentMountNoteBookGetRouletteActionIdDetour> _agentMountNoteBookGetRouletteActionIdHook;
    private readonly Hook<AgentMountNoteBookIsRouletteAvailableDetour> _agentMountNoteBookIsRouletteAvailableHook;

    private unsafe delegate bool AgentMountNoteBookUseRouletteDetour(AgentInterface* @this, uint rouletteIndex);
    private unsafe delegate uint AgentMountNoteBookGetRouletteIconDetour(AgentInterface* @this, uint rouletteIndex);
    private unsafe delegate uint AgentMountNoteBookGetRouletteActionIdDetour(AgentInterface* @this, uint rouletteIndex);
    private unsafe delegate bool AgentMountNoteBookIsRouletteAvailableDetour(AgentInterface* @this, uint rouletteIndex);

    public unsafe AgentMountNoteBookHooks(PluginServices services)
    {
        _services = services;

        // AgentModule.Instance() and the MountNotebook agent can both legitimately be null before the UI module
        // has finished initializing. Dereferencing either would be an uncatchable AccessViolationException that
        // crashes the whole game, so fail with a managed exception instead (the plugin reports it as a clean
        // load error rather than taking the client down).
        AgentModule* agentModule = AgentModule.Instance();
        AgentInterface* agent = agentModule == null ? null : agentModule->GetAgentByInternalId(AgentId.MountNotebook);
        if (agent == null || agent->VirtualTable == null)
        {
            throw new InvalidOperationException("MountNotebook agent is not available yet; cannot install roulette hooks.");
        }

        var vtable = (AgentMountNoteBookVTable*)agent->VirtualTable;
        _agentAddress = (nint)agent;
        _vtableAddress = (nint)vtable;
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
    }

    internal void Enable()
    {
        _agentMountNoteBookUseRouletteHook.Enable();
        _agentMountNoteBookGetRouletteIconHook.Enable();
        _agentMountNoteBookGetRouletteActionIdHook.Enable();
        _agentMountNoteBookIsRouletteAvailableHook.Enable();

        // Information level (LogLevel 2 catches it) so the hardcoded vtable offsets can be verified on the live
        // TC client: if an offset resolved to the wrong function, these addresses will not line up with the real
        // MountNotebook agent vtable and the roulette-button behaviour will visibly misbehave.
        _services.PluginLog.Information(
            $"[飛行輪盤鈕] hook 掛載 agent=0x{_agentAddress:X} vtable=0x{_vtableAddress:X} " +
            $"UseRoulette=0x{_agentMountNoteBookUseRouletteHook.Address:X} " +
            $"IsRouletteAvailable=0x{_agentMountNoteBookIsRouletteAvailableHook.Address:X} " +
            $"GetRouletteActionId=0x{_agentMountNoteBookGetRouletteActionIdHook.Address:X} " +
            $"GetRouletteIcon=0x{_agentMountNoteBookGetRouletteIconHook.Address:X}");
    }

    internal void Disable()
    {
        _agentMountNoteBookUseRouletteHook.Disable();
        _agentMountNoteBookGetRouletteIconHook.Disable();
        _agentMountNoteBookGetRouletteActionIdHook.Disable();
        _agentMountNoteBookIsRouletteAvailableHook.Disable();
    }

    private unsafe bool OnIsRouletteAvailable(AgentInterface* @this, uint rouletteIndex)
    {
        _services.PluginLog.Debug($"OnIsRouletteAvailable(this, {rouletteIndex})");
        if (rouletteIndex == 1)
        {
            rouletteIndex = 0;
        }

        return _agentMountNoteBookIsRouletteAvailableHook.Original(@this, rouletteIndex);
    }

    private unsafe uint OnGetRouletteActionId(AgentInterface* @this, uint rouletteIndex)
    {
        _services.PluginLog.Debug($"OnGetRouletteActionId(this, {rouletteIndex})");
        return rouletteIndex == 1
            ? 24
            : _agentMountNoteBookGetRouletteActionIdHook.Original(@this, rouletteIndex);
    }

    private unsafe uint OnGetRouletteIcon(AgentInterface* @this, uint rouletteIndex)
    {
        _services.PluginLog.Debug($"OnGetRouletteIcon(this, {rouletteIndex})");
        return rouletteIndex == 1
            ? 122
            : _agentMountNoteBookGetRouletteIconHook.Original(@this, rouletteIndex);
    }

    private unsafe bool OnUseRoulette(AgentInterface* @this, uint rouletteIndex)
    {
        _services.PluginLog.Debug($"OnUseRoulette(this, {rouletteIndex})");
        return rouletteIndex == 1
            ? ActionManager.Instance()->UseAction(ActionType.GeneralAction, 24)
            : _agentMountNoteBookUseRouletteHook.Original(@this, rouletteIndex);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            _agentMountNoteBookUseRouletteHook.Dispose();
            _agentMountNoteBookGetRouletteIconHook.Dispose();
            _agentMountNoteBookGetRouletteActionIdHook.Dispose();
            _agentMountNoteBookIsRouletteAvailableHook.Dispose();

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
