using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using NeeView.Effects.Shaders;

namespace NeeView
{
    public sealed class ShaderEffectInfo
    {
        public required string Name { get; init; }
        public required string FullPath { get; init; }
        public required string RelativePath { get; init; }
        public string SortName { get; init; } = "";
        public bool IsEnabled { get; init; }
    }

    /// <summary>
    /// Discovers MagpieFX effects under the effects folder.
    /// </summary>
    public sealed class ShaderEffectCatalog
    {
        private static readonly HashSet<string> InitialEffects = new(StringComparer.OrdinalIgnoreCase)
        {
            "Common/Nearest",
            "Common/Bilinear",
            "Common/Bicubic",
            "Common/Lanczos",
            "Common/Jinc",
            "Common/SGSR",
            "Downscale/SSimDownscaler",
            "Upscale/FSR/FSR_EASU",
            "Upscale/FSR/FSR_RCAS",
            "Upscale/NIS/NIS",
            "Upscale/Anime4K/Anime4K_Upscale_S",
            "Upscale/Anime4K/Anime4K_Upscale_L",
        };

        private readonly Lazy<ReadOnlyCollection<ShaderEffectInfo>> _effects;

        public ShaderEffectCatalog(string effectsRootPath)
        {
            EffectsRootPath = effectsRootPath;
            _effects = new(() => Load(effectsRootPath));
        }

        public string EffectsRootPath { get; }

        public ReadOnlyCollection<ShaderEffectInfo> Effects => _effects.Value;

        private static ReadOnlyCollection<ShaderEffectInfo> Load(string root)
        {
            var result = new List<ShaderEffectInfo>();
            if (!Directory.Exists(root))
            {
                return new ReadOnlyCollection<ShaderEffectInfo>(result);
            }

            foreach (var path in Directory.EnumerateFiles(root, "*.hlsl", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                var key = relative[..^".hlsl".Length];
                var enabled = InitialEffects.Contains(key) || InitialEffects.Contains(GetLegacyKey(key));
                if (!enabled)
                {
                    continue;
                }

                result.Add(new ShaderEffectInfo
                {
                    Name = GetDisplayName(key),
                    FullPath = path,
                    RelativePath = relative[..^".hlsl".Length],
                    SortName = GetDisplayName(key),
                    IsEnabled = true,
                });
            }

            return new ReadOnlyCollection<ShaderEffectInfo>(
                result.OrderBy(e => e.SortName, StringComparer.CurrentCultureIgnoreCase).ToList());
        }

        public static string GetDisplayName(string key)
        {
            return key switch
            {
                "Common/Nearest" => "Nearest",
                "Common/Bilinear" => "Bilinear",
                "Common/Bicubic" => "Bicubic",
                "Common/Lanczos" => "Lanczos",
                "Common/Jinc" => "Jinc",
                "Common/SGSR" => "SGSR",
                "Downscale/SSimDownscaler" => "SSimDownscaler",
                "Upscale/FSR/FSR_EASU" => "FSR EASU",
                "Upscale/FSR/FSR_RCAS" => "FSR RCAS",
                "Upscale/NIS/NIS" => "NIS",
                "Upscale/Anime4K/Anime4K_Upscale_S" => "Anime4K Upscale S",
                "Upscale/Anime4K/Anime4K_Upscale_L" => "Anime4K Upscale L",
                _ => key.Replace('/', ' '),
            };
        }

        public static MagpieEffectDesc GetFallbackEffect(string effectsRootPath)
        {
            var path = Path.Combine(effectsRootPath, "Common", "Lanczos.hlsl");
            var desc = MagpieEffectParser.ParseFile(path);
            var compiler = new MagpieEffectCompiler(Path.Combine(Path.GetTempPath(), "NeeView", "ShaderCache"), effectsRootPath);
            return compiler.Compile(desc, new MagpieCompileOptions());
        }

        public static string FindEffectsRoot()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "effects");
            if (Directory.Exists(path))
            {
                return path;
            }
            return Path.Combine(System.Environment.CurrentDirectory, "effects");
        }

        private static string GetLegacyKey(string key)
        {
            return key switch
            {
                var value when value.StartsWith("Common/", StringComparison.OrdinalIgnoreCase) => value["Common/".Length..],
                var value when value.StartsWith("Downscale/", StringComparison.OrdinalIgnoreCase) => value["Downscale/".Length..],
                var value when value.StartsWith("Upscale/FSR/", StringComparison.OrdinalIgnoreCase) => "FSR/" + value["Upscale/FSR/".Length..],
                var value when value.StartsWith("Upscale/NIS/", StringComparison.OrdinalIgnoreCase) => "NIS/" + value["Upscale/NIS/".Length..],
                var value when value.StartsWith("Upscale/Anime4K/", StringComparison.OrdinalIgnoreCase) => "Anime4K/" + value["Upscale/Anime4K/".Length..],
                _ => key,
            };
        }
    }
}
