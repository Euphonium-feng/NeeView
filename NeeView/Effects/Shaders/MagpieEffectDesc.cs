using System;
using System.Collections.Generic;

namespace NeeView.Effects.Shaders
{
    public enum MagpieEffectTextureFormat
    {
        R32G32B32A32_FLOAT,
        R16G16B16A16_FLOAT,
        R16G16B16A16_UNORM,
        R16G16B16A16_SNORM,
        R32G32_FLOAT,
        R10G10B10A2_UNORM,
        R11G11B10_FLOAT,
        R8G8B8A8_UNORM,
        R8G8B8A8_SNORM,
        R16G16_FLOAT,
        R16G16_UNORM,
        R16G16_SNORM,
        R32_FLOAT,
        R8G8_UNORM,
        R8G8_SNORM,
        R16_FLOAT,
        R16_UNORM,
        R16_SNORM,
        R8_UNORM,
        R8_SNORM,
        Unknown
    }

    public enum MagpieSamplerFilter
    {
        Linear,
        Point,
    }

    public enum MagpieSamplerAddress
    {
        Clamp,
        Wrap,
    }

    public sealed class MagpieEffectTextureDesc
    {
        public string Name { get; set; } = "";
        public string WidthExpression { get; set; } = "";
        public string HeightExpression { get; set; } = "";
        public MagpieEffectTextureFormat Format { get; set; } = MagpieEffectTextureFormat.Unknown;
        public string Source { get; set; } = "";
    }

    public sealed class MagpieSamplerDesc
    {
        public string Name { get; set; } = "";
        public MagpieSamplerFilter Filter { get; set; } = MagpieSamplerFilter.Linear;
        public MagpieSamplerAddress Address { get; set; } = MagpieSamplerAddress.Clamp;
    }

    public sealed class MagpieParameterDesc
    {
        public string Name { get; set; } = "";
        public string Label { get; set; } = "";
        public bool IsInteger { get; set; }
        public double DefaultValue { get; set; }
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double Step { get; set; }
    }

    public sealed class MagpiePassDesc
    {
        public int Index { get; set; }
        public string Description { get; set; } = "";
        public bool IsPixelStyle { get; set; }
        public List<int> Inputs { get; } = new();
        public List<int> Outputs { get; } = new();
        public int[] NumThreads { get; set; } = [1, 1, 1];
        public int BlockWidth { get; set; } = 1;
        public int BlockHeight { get; set; } = 1;
        public byte[] Bytecode { get; set; } = [];
        public string Source { get; set; } = "";
    }

    public sealed class MagpieEffectDesc
    {
        public string Name { get; set; } = "";
        public string SortName { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public List<MagpieParameterDesc> Parameters { get; } = new();
        public List<MagpieEffectTextureDesc> Textures { get; } = new();
        public List<MagpieSamplerDesc> Samplers { get; } = new();
        public List<MagpiePassDesc> Passes { get; } = new();
        public bool SupportsFp16 { get; set; }
        public bool UsesMulAdd { get; set; }
        public bool UsesDynamic { get; set; }
        public string CommonSource { get; set; } = "";
        public Dictionary<string, string> Includes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public MagpieEffectTextureDesc Output => Textures.Count > 1 ? Textures[1] : Textures[0];

        public bool CanScale => string.IsNullOrWhiteSpace(Output.WidthExpression);
    }
}
