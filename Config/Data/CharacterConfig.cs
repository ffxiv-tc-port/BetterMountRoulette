namespace BetterMountRoulette.Config.Data;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

internal sealed class CharacterConfig
{
    [JsonIgnore]
    public bool IsNew { get; set; }

    public bool IncludeNewMounts { get; set; } = true;

    public List<MountGroup> Groups { get; set; } = [];

    public string? MountRouletteGroup { get; set; }

    public bool RevealMountsNormal { get; set; }

    public bool RevealMountsFlying { get; set; }

    public string? FlyingMountRouletteGroup { get; set; }

    public bool SuppressChatErrors { get; set; }

    // Default OFF: enabling this installs 4 hooks at hardcoded MountNotebook agent vtable offsets that are not
    // verified against the TC client. Per fleet redline (unproven assumption + hook timing = deployment gate),
    // the vtable-touching feature must be opt-in so the default state cannot crash. Main feature (mount-group
    // roulette via the UseAction hook) is unaffected and stays on.
    public bool EnableFlyingRouletteButton { get; set; }

    [JsonIgnore]
    public bool HasNonDefaultGroups => Groups.Count > 1;

    public void CopyFrom(CharacterConfig other)
    {
        IncludeNewMounts = other.IncludeNewMounts;
        Groups = other.Groups;
        MountRouletteGroup = other.MountRouletteGroup;
        FlyingMountRouletteGroup = other.FlyingMountRouletteGroup;
    }

    [SuppressMessage(
        "Globalization",
        "CA1309:Use ordinal string comparison",
        Justification = "We actually want string normalization here, to ensure same behavior as in the duplicate check when renaming or adding a group")]
    public MountGroup? GetMountGroup(string name)
    {
        return Groups.FirstOrDefault(x => x.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));
    }
}
