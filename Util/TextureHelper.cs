namespace BetterMountRoulette.Util;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;

using System;
using System.Collections.Generic;

internal sealed class TextureHelper(PluginServices services)
{
    private readonly PluginServices _services = services;
    private readonly Dictionary<string, string> _uldPaths = new(StringComparer.Ordinal);
    private ITextureProvider TextureProvider => _services.TextureProvider;

    // UI thread only. The mount grid asks for the same overlay once per mount per frame; build each path once.
    public ImTextureID LoadUldTexture(string name)
    {
        if (!_uldPaths.TryGetValue(name, out string? path))
        {
            path = $"ui/uld/{name}_hr1.tex";
            _uldPaths[name] = path;
        }

        return TextureProvider.GetFromGame(path).GetWrapOrEmpty().Handle;
    }

    public ImTextureID LoadIconTexture(uint id)
    {
        return TextureProvider.GetFromGameIcon(new GameIconLookup(id)).GetWrapOrEmpty().Handle;
    }
}
