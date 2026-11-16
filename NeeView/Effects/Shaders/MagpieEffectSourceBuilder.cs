using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NeeView.Effects.Shaders
{
    /// <summary>
    /// Converts MagpieFX blocks into an HLSL compute shader source.
    /// </summary>
    public static class MagpieEffectSourceBuilder
    {
        private static readonly string[] NumberSuffixes = ["1", "2", "3", "4"];

        public static string BuildPassSource(MagpieEffectDesc desc, int passIndex, IReadOnlyDictionary<string, double>? parameters = null)
        {
            var pass = desc.Passes[passIndex - 1];
            var builder = new StringBuilder();
            builder.AppendLine(BuildConstantBuffer(desc, parameters));
            builder.AppendLine();

            for (var i = 0; i < pass.Inputs.Count; i++)
            {
                var texture = desc.Textures[pass.Inputs[i]];
                builder.AppendLine($"Texture2D<{GetSrvTexelType(texture.Format)}> {texture.Name} : register(t{i});");
            }
            for (var i = 0; i < pass.Outputs.Count; i++)
            {
                var texture = desc.Textures[pass.Outputs[i]];
                builder.AppendLine($"RWTexture2D<{GetUavTexelType(texture.Format)}> {texture.Name} : register(u{i});");
            }
            foreach (var sampler in desc.Samplers.Select((value, index) => (value, index)))
            {
                builder.AppendLine($"SamplerState {sampler.value.Name} : register(s{sampler.index});");
            }
            builder.AppendLine();
            builder.AppendLine("uint __Bfe(uint src, uint off, uint bits) { uint mask = (1u << bits) - 1; return (src >> off) & mask; }");
            builder.AppendLine("uint __BfiM(uint src, uint ins, uint bits) { uint mask = (1u << bits) - 1; return (ins & mask) | (src & (~mask)); }");
            builder.AppendLine("uint2 Rmp8x8(uint a) { return uint2(__Bfe(a, 1u, 3u), __BfiM(__Bfe(a, 3u, 3u), a, 1u)); }");
            builder.AppendLine("uint2 GetInputSize() { return __inputSize; }");
            builder.AppendLine("float2 GetInputPt() { return __inputPt; }");
            builder.AppendLine("uint2 GetOutputSize() { return __outputSize; }");
            builder.AppendLine("float2 GetOutputPt() { return __outputPt; }");
            builder.AppendLine("float2 GetScale() { return __scale; }");
            if (desc.UsesMulAdd)
            {
                builder.AppendLine(BuildMulAddFunctions());
            }
            builder.AppendLine();
            builder.AppendLine(BuildCommonBlocks(desc));
            builder.AppendLine(BuildPassBlock(desc, passIndex));
            builder.AppendLine();
            builder.AppendLine(BuildEntryPoint(desc, passIndex));

            return builder.ToString();
        }

        public static IReadOnlyDictionary<string, string> BuildMacros(MagpieEffectDesc desc, int passIndex, bool enableFp16)
        {
            var pass = desc.Passes[passIndex - 1];
            var result = new Dictionary<string, string>
            {
                ["MP_BLOCK_WIDTH"] = pass.BlockWidth.ToString(CultureInfo.InvariantCulture),
                ["MP_BLOCK_HEIGHT"] = pass.BlockHeight.ToString(CultureInfo.InvariantCulture),
                ["MP_NUM_THREADS_X"] = pass.NumThreads[0].ToString(CultureInfo.InvariantCulture),
                ["MP_NUM_THREADS_Y"] = pass.NumThreads[1].ToString(CultureInfo.InvariantCulture),
                ["MP_NUM_THREADS_Z"] = pass.NumThreads[2].ToString(CultureInfo.InvariantCulture),
            };

            if (pass.IsPixelStyle)
            {
                result["MP_PS_STYLE"] = "";
            }

            var useFp16 = enableFp16 && desc.SupportsFp16;
            if (useFp16)
            {
                result["MP_FP16"] = "";
                result["MF"] = "min16float";
            }
            else
            {
                result["MF"] = "float";
            }

            foreach (var first in NumberSuffixes)
            {
                var typePrefix = useFp16 ? "min16float" : "float";
                result[$"MF{first}"] = typePrefix + first;
                foreach (var second in NumberSuffixes)
                {
                    result[$"MF{first}x{second}"] = $"{typePrefix}{first}x{second}";
                }
            }

            return result;
        }

        public static string GetSrvTexelType(MagpieEffectTextureFormat format)
        {
            return format switch
            {
                MagpieEffectTextureFormat.R32G32B32A32_FLOAT => "float4",
                MagpieEffectTextureFormat.R32G32_FLOAT => "float2",
                MagpieEffectTextureFormat.R11G11B10_FLOAT => "MF3",
                MagpieEffectTextureFormat.R32_FLOAT => "float",
                MagpieEffectTextureFormat.R16G16B16A16_FLOAT or MagpieEffectTextureFormat.R16G16B16A16_UNORM
                    or MagpieEffectTextureFormat.R16G16B16A16_SNORM or MagpieEffectTextureFormat.R10G10B10A2_UNORM
                    or MagpieEffectTextureFormat.R8G8B8A8_UNORM or MagpieEffectTextureFormat.R8G8B8A8_SNORM => "MF4",
                MagpieEffectTextureFormat.R16G16_FLOAT or MagpieEffectTextureFormat.R16G16_UNORM
                    or MagpieEffectTextureFormat.R16G16_SNORM or MagpieEffectTextureFormat.R8G8_UNORM
                    or MagpieEffectTextureFormat.R8G8_SNORM => "MF2",
                MagpieEffectTextureFormat.R16_FLOAT or MagpieEffectTextureFormat.R16_UNORM
                    or MagpieEffectTextureFormat.R16_SNORM or MagpieEffectTextureFormat.R8_UNORM
                    or MagpieEffectTextureFormat.R8_SNORM => "MF",
                _ => "float4",
            };
        }

        public static string GetUavTexelType(MagpieEffectTextureFormat format)
        {
            return format switch
            {
                MagpieEffectTextureFormat.R16G16B16A16_UNORM or MagpieEffectTextureFormat.R8G8B8A8_UNORM
                    or MagpieEffectTextureFormat.R10G10B10A2_UNORM => "unorm " + GetSrvTexelType(format),
                MagpieEffectTextureFormat.R16G16B16A16_SNORM or MagpieEffectTextureFormat.R8G8B8A8_SNORM
                    or MagpieEffectTextureFormat.R16G16_SNORM or MagpieEffectTextureFormat.R8G8_SNORM
                    or MagpieEffectTextureFormat.R16_SNORM or MagpieEffectTextureFormat.R8_SNORM => "snorm " + GetSrvTexelType(format),
                _ => GetSrvTexelType(format),
            };
        }

        private static string BuildConstantBuffer(MagpieEffectDesc desc, IReadOnlyDictionary<string, double>? parameters)
        {
            var builder = new StringBuilder();
            builder.AppendLine("cbuffer __CB1 : register(b0) {");
            builder.AppendLine("    uint2 __inputSize;");
            builder.AppendLine("    uint2 __outputSize;");
            builder.AppendLine("    float2 __inputPt;");
            builder.AppendLine("    float2 __outputPt;");
            builder.AppendLine("    float2 __scale;");
            for (var i = 0; i < desc.Passes.Count - 1; i++)
            {
                if (desc.Passes[i].IsPixelStyle)
                {
                    builder.AppendLine($"    uint2 __pass{i + 1}OutputSize;");
                    builder.AppendLine($"    float2 __pass{i + 1}OutputPt;");
                }
            }
            foreach (var parameter in desc.Parameters)
            {
                builder.AppendLine($"    {(parameter.IsInteger ? "int" : "float")} {parameter.Name};");
            }
            builder.AppendLine("}");

            if (!desc.UsesDynamic)
            {
                return builder.ToString();
            }

            builder.AppendLine("cbuffer __CB2 : register(b1) {");
            builder.AppendLine("    uint __frameCount;");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string BuildCommonBlocks(MagpieEffectDesc desc)
        {
            return desc.CommonSource;
        }

        private static string BuildPassBlock(MagpieEffectDesc desc, int passIndex)
        {
            return desc.Passes[passIndex - 1].Source;
        }

        private static string BuildEntryPoint(MagpieEffectDesc desc, int passIndex)
        {
            var pass = desc.Passes[passIndex - 1];
            var output = desc.Textures[pass.Outputs[0]];
            if (pass.IsPixelStyle)
            {
                var isLast = passIndex == desc.Passes.Count;
                var outputSize = isLast ? "__outputSize" : $"__pass{passIndex}OutputSize";
                var outputPt = isLast ? "__outputPt" : $"__pass{passIndex}OutputPt";
                return $@"[numthreads(64, 1, 1)]
void __M(uint3 tid : SV_GroupThreadID, uint3 gid : SV_GroupID) {{
    uint2 gxy = (gid.xy << 4u) + Rmp8x8(tid.x);
    if (gxy.x >= {outputSize}.x || gxy.y >= {outputSize}.y) return;
    float2 pos = (gxy + 0.5f) * {outputPt};
    float2 step = 8 * {outputPt};

    {output.Name}[gxy] = Pass{passIndex}(pos);

    gxy.x += 8u;
    pos.x += step.x;
    if (gxy.x < {outputSize}.x && gxy.y < {outputSize}.y) {{
        {output.Name}[gxy] = Pass{passIndex}(pos);
    }}

    gxy.y += 8u;
    pos.y += step.y;
    if (gxy.x < {outputSize}.x && gxy.y < {outputSize}.y) {{
        {output.Name}[gxy] = Pass{passIndex}(pos);
    }}

    gxy.x -= 8u;
    pos.x -= step.x;
    if (gxy.x < {outputSize}.x && gxy.y < {outputSize}.y) {{
        {output.Name}[gxy] = Pass{passIndex}(pos);
    }}
}}";
            }

            var blockStart = pass.BlockWidth == pass.BlockHeight && System.Numerics.BitOperations.IsPow2((uint)pass.BlockWidth)
                ? $"(gid.xy << {(int)Math.Log2(pass.BlockWidth)})"
                : $"gid.xy * uint2({pass.BlockWidth}, {pass.BlockHeight})";
            return $@"[numthreads({pass.NumThreads[0]}, {pass.NumThreads[1]}, {pass.NumThreads[2]})]
void __M(uint3 tid : SV_GroupThreadID, uint3 gid : SV_GroupID) {{
    Pass{passIndex}({blockStart}, tid);
}}";
        }

        private static string BuildMulAddFunctions()
        {
            var builder = new StringBuilder();
            for (var left = 2; left <= 4; left++)
            {
                for (var right = 2; right <= 4; right++)
                {
                    builder.AppendLine($"MF{right} MulAdd(MF{left} x, MF{left}x{right} y, MF{right} a) {{");
                    builder.AppendLine($"    MF{right} result = a;");
                    for (var i = 0; i < left; i++)
                    {
                        builder.AppendLine($"    result = mad(x[{i}], y[{i}], result);");
                    }
                    builder.AppendLine("    return result;");
                    builder.AppendLine("}");
                }
            }
            return builder.ToString();
        }
    }
}
