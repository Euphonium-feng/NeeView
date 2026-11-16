using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using D3D11 = Vortice.Direct3D11.D3D11;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace NeeView.Effects.Shaders
{
    /// <summary>
    /// GPU shader-chain execution. All resources are recreated as needed.
    /// </summary>
    public sealed class ShaderChainRenderer : IDisposable
    {
        private readonly object _lock = new();
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private bool _disposed;

        public bool IsAvailable
        {
            get
            {
                lock (_lock)
                {
                    EnsureDevice();
                    return _device is not null;
                }
            }
        }

        public BitmapSource Render(BitmapSource source, IReadOnlyList<ShaderRenderItem> chain, Size targetSize)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (chain is null) throw new ArgumentNullException(nameof(chain));
            if (targetSize.Width <= 0 || targetSize.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetSize));
            }

            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                EnsureDevice();
                if (_device is null || _context is null)
                {
                    throw new InvalidOperationException("D3D11 device is unavailable.");
                }

                var inputBytes = GetRgbaBytes(source);
                var inputWidth = source.PixelWidth;
                var inputHeight = source.PixelHeight;

                using var inputTexture = CreateTexture(_device, inputWidth, inputHeight, Format.R8G8B8A8_UNorm, BindFlags.ShaderResource, inputBytes);
                var currentTexture = inputTexture;
                var currentWidth = inputWidth;
                var currentHeight = inputHeight;
                var ownedTextures = new List<ID3D11Texture2D>();

                try
                {
                    for (var chainIndex = 0; chainIndex < chain.Count; chainIndex++)
                    {
                        var item = chain[chainIndex];
                        var desc = item.Effect;
                        var isLast = chainIndex == chain.Count - 1;
                        var hasFixedScaleAfter = chain.Skip(chainIndex + 1).Any(e => !e.Effect.CanScale);
                        var outputWidth = desc.CanScale
                            ? (!hasFixedScaleAfter ? (int)Math.Round(targetSize.Width) : currentWidth)
                            : EvalSize(desc.Output.WidthExpression, currentWidth, currentHeight, targetSize.Width, targetSize.Height);
                        var outputHeight = desc.CanScale
                            ? (!hasFixedScaleAfter ? (int)Math.Round(targetSize.Height) : currentHeight)
                            : EvalSize(desc.Output.HeightExpression, currentWidth, currentHeight, targetSize.Width, targetSize.Height);
                        if (outputWidth <= 0 || outputHeight <= 0)
                        {
                            throw new InvalidOperationException($"Shader {desc.Name} produced an invalid output size.");
                        }

                        var outputTexture = CreateTexture(_device, outputWidth, outputHeight, ToDxgiFormat(desc.Output.Format), BindFlags.ShaderResource | BindFlags.UnorderedAccess, null);
                        ownedTextures.Add(outputTexture);
                        DrawEffect(desc, item.Parameters, inputTexture: currentTexture, outputTexture, currentWidth, currentHeight, outputWidth, outputHeight);
                        currentTexture = outputTexture;
                        currentWidth = outputWidth;
                        currentHeight = outputHeight;
                    }

                    if (currentWidth != (int)Math.Round(targetSize.Width) || currentHeight != (int)Math.Round(targetSize.Height))
                    {
                        if (chain.Count > 0)
                        {
                            var fitDesc = ShaderEffectCatalog.GetFallbackEffect(ShaderEffectCatalog.FindEffectsRoot());
                            var fitTexture = CreateTexture(_device, (int)Math.Round(targetSize.Width), (int)Math.Round(targetSize.Height), Format.R8G8B8A8_UNorm, BindFlags.ShaderResource | BindFlags.UnorderedAccess, null);
                            ownedTextures.Add(fitTexture);
                            DrawEffect(fitDesc, new Dictionary<string, double>(), currentTexture, fitTexture, currentWidth, currentHeight, (int)Math.Round(targetSize.Width), (int)Math.Round(targetSize.Height));
                            currentTexture = fitTexture;
                            currentWidth = (int)Math.Round(targetSize.Width);
                            currentHeight = (int)Math.Round(targetSize.Height);
                        }
                    }

                    var result = ReadTexture(_context, currentTexture, currentWidth, currentHeight);
                    if (currentTexture.Description.Format == Format.R8G8B8A8_UNorm)
                    {
                        for (var i = 0; i + 3 < result.Length; i += 4)
                        {
                            (result[i], result[i + 2]) = (result[i + 2], result[i]);
                        }
                    }
                    var bitmap = BitmapSource.Create(
                        currentWidth,
                        currentHeight,
                        96,
                        96,
                        PixelFormats.Bgra32,
                        null,
                        result,
                        currentWidth * 4);
                    bitmap.Freeze();
                    return bitmap;
                }
                finally
                {
                    foreach (var texture in ownedTextures)
                    {
                        if (!ReferenceEquals(texture, inputTexture))
                        {
                            texture.Dispose();
                        }
                    }
                }
            }
        }

        private void DrawEffect(MagpieEffectDesc desc, IReadOnlyDictionary<string, double> parameters, ID3D11Texture2D inputTexture, ID3D11Texture2D outputTexture, int inputWidth, int inputHeight, int outputWidth, int outputHeight)
        {
            if (_device is null || _context is null)
            {
                throw new InvalidOperationException("D3D11 device is unavailable.");
            }

            var textures = new List<ID3D11Texture2D> { inputTexture, outputTexture };
            for (var i = 2; i < desc.Textures.Count; i++)
            {
                var textureDesc = desc.Textures[i];
                var width = Math.Max(1, EvalSize(textureDesc.WidthExpression, inputWidth, inputHeight, outputWidth, outputHeight));
                var height = Math.Max(1, EvalSize(textureDesc.HeightExpression, inputWidth, inputHeight, outputWidth, outputHeight));
                textures.Add(CreateTexture(_device, width, height, ToDxgiFormat(textureDesc.Format), BindFlags.ShaderResource | BindFlags.UnorderedAccess, null));
            }

            try
            {
                var constants = BuildConstants(desc, parameters, textures, inputWidth, inputHeight, outputWidth, outputHeight);
                using var constantBuffer = _device.CreateBuffer(constants, BindFlags.ConstantBuffer, ResourceUsage.Default);
                var samplers = desc.Samplers.Select(CreateSampler).ToArray();
                try
                {
                    _context.CSSetConstantBuffer(0, constantBuffer);
                    _context.CSSetSamplers(0, samplers);

                    foreach (var pass in desc.Passes)
                    {
                        using var shader = _device.CreateComputeShader(pass.Bytecode);
                        var srvs = pass.Inputs.Select(index => _device.CreateShaderResourceView(textures[index])).ToArray();
                        var uavs = pass.Outputs.Select(index => _device.CreateUnorderedAccessView(textures[index])).ToArray();
                        try
                        {
                            _context.CSSetShader(shader);
                            _context.CSSetShaderResources(0, srvs);
                            _context.CSSetUnorderedAccessViews(0, uavs);
                            var passOutput = textures[pass.Outputs[0]].Description;
                            _context.Dispatch(
                                (uint)Math.Max(1, ((int)passOutput.Width + pass.BlockWidth - 1) / pass.BlockWidth),
                                (uint)Math.Max(1, ((int)passOutput.Height + pass.BlockHeight - 1) / pass.BlockHeight),
                                1);
                            _context.CSUnsetUnorderedAccessViews(0, (uint)uavs.Length);
                        }
                        finally
                        {
                            foreach (var view in srvs) view.Dispose();
                            foreach (var view in uavs) view.Dispose();
                        }
                    }
                }
                finally
                {
                    _context.CSSetShader(null);
                    foreach (var sampler in samplers) sampler.Dispose();
                }
            }
            finally
            {
                foreach (var texture in textures.Skip(2))
                {
                    texture.Dispose();
                }
            }
        }

        private ID3D11SamplerState CreateSampler(MagpieSamplerDesc desc)
        {
            if (_device is null) throw new InvalidOperationException();
            return _device.CreateSamplerState(new SamplerDescription
            {
                Filter = desc.Filter == MagpieSamplerFilter.Point ? Filter.MinMagMipPoint : Filter.MinMagMipLinear,
                AddressU = desc.Address == MagpieSamplerAddress.Wrap ? TextureAddressMode.Wrap : TextureAddressMode.Clamp,
                AddressV = desc.Address == MagpieSamplerAddress.Wrap ? TextureAddressMode.Wrap : TextureAddressMode.Clamp,
                AddressW = desc.Address == MagpieSamplerAddress.Wrap ? TextureAddressMode.Wrap : TextureAddressMode.Clamp,
                ComparisonFunc = ComparisonFunction.Never,
                MaxLOD = float.MaxValue,
            });
        }

        private static byte[] BuildConstants(MagpieEffectDesc desc, IReadOnlyDictionary<string, double> parameters, IReadOnlyList<ID3D11Texture2D> textures, int inputWidth, int inputHeight, int outputWidth, int outputHeight)
        {
            var list = new List<uint>
            {
                (uint)inputWidth,
                (uint)inputHeight,
                (uint)outputWidth,
                (uint)outputHeight,
                BitConverter.SingleToUInt32Bits(1.0f / inputWidth),
                BitConverter.SingleToUInt32Bits(1.0f / inputHeight),
                BitConverter.SingleToUInt32Bits(1.0f / outputWidth),
                BitConverter.SingleToUInt32Bits(1.0f / outputHeight),
                BitConverter.SingleToUInt32Bits((float)outputWidth / inputWidth),
                BitConverter.SingleToUInt32Bits((float)outputHeight / inputHeight),
            };
            for (var i = 0; i < desc.Passes.Count - 1; i++)
            {
                if (!desc.Passes[i].IsPixelStyle || desc.Passes[i].Outputs.Count == 0)
                {
                    continue;
                }
                var passOutput = textures[desc.Passes[i].Outputs[0]].Description;
                list.Add(passOutput.Width);
                list.Add(passOutput.Height);
                list.Add(BitConverter.SingleToUInt32Bits(1.0f / passOutput.Width));
                list.Add(BitConverter.SingleToUInt32Bits(1.0f / passOutput.Height));
            }
            foreach (var parameter in desc.Parameters)
            {
                var value = parameters.TryGetValue(parameter.Name, out var configured)
                    ? configured
                    : parameter.DefaultValue;
                list.Add(parameter.IsInteger ? (uint)(int)Math.Round(value) : BitConverter.SingleToUInt32Bits((float)value));
            }
            while (list.Count % 4 != 0)
            {
                list.Add(0);
            }
            var bytes = new byte[list.Count * sizeof(uint)];
            for (var i = 0; i < list.Count; i++)
            {
                BitConverter.GetBytes(list[i]).CopyTo(bytes, i * sizeof(uint));
            }
            return bytes;
        }

        private static ID3D11Texture2D CreateTexture(ID3D11Device device, int width, int height, Format format, BindFlags bindFlags, byte[]? data)
        {
            if (data is null)
            {
                return device.CreateTexture2D(format, (uint)width, (uint)height, 1, 1, null, bindFlags);
            }

            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                return device.CreateTexture2D(
                    format,
                    (uint)width,
                    (uint)height,
                    1,
                    1,
                    [new SubresourceData(handle.AddrOfPinnedObject(), (uint)(width * 4))],
                    bindFlags);
            }
            finally
            {
                handle.Free();
            }
        }

        private static byte[] ReadTexture(ID3D11DeviceContext context, ID3D11Texture2D texture, int width, int height)
        {
            using var staging = CreateStaging(context, texture, width, height);
            var mapped = context.Map(staging, 0, MapMode.Read, MapFlags.None);
            try
            {
                var bytes = new byte[width * height * 4];
                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(
                        mapped.DataPointer + (int)(y * mapped.RowPitch),
                        bytes,
                        y * width * 4,
                        width * 4);
                }
                return bytes;
            }
            finally
            {
                context.Unmap(staging, 0);
            }
        }

        private static ID3D11Texture2D CreateStaging(ID3D11DeviceContext context, ID3D11Texture2D texture, int width, int height)
        {
            var sourceDesc = texture.Description;
            var desc = new Texture2DDescription(
                sourceDesc.Format,
                (uint)width,
                (uint)height,
                1,
                1,
                BindFlags.None,
                ResourceUsage.Staging,
                CpuAccessFlags.Read,
                1,
                0,
                ResourceOptionFlags.None);
            var device = context.Device;
            var staging = device.CreateTexture2D(desc);
            context.CopyResource(staging, texture);
            context.Flush();
            return staging;
        }

        private static byte[] GetRgbaBytes(BitmapSource source)
        {
            var converted = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var bytes = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(bytes, stride, 0);
            for (var i = 0; i + 3 < bytes.Length; i += 4)
            {
                (bytes[i], bytes[i + 2]) = (bytes[i + 2], bytes[i]);
            }
            return bytes;
        }

        private static int EvalSize(string expression, int inputWidth, int inputHeight, double outputWidth, double outputHeight)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return (int)Math.Round(outputWidth);
            }
            var value = MagpieExpression.Evaluate(expression, new Dictionary<string, double>
            {
                ["INPUT_WIDTH"] = inputWidth,
                ["INPUT_HEIGHT"] = inputHeight,
                ["OUTPUT_WIDTH"] = outputWidth,
                ["OUTPUT_HEIGHT"] = outputHeight,
            });
            return (int)Math.Round(value);
        }

        private static Format ToDxgiFormat(MagpieEffectTextureFormat format)
        {
            return format switch
            {
                MagpieEffectTextureFormat.R32G32B32A32_FLOAT => Format.R32G32B32A32_Float,
                MagpieEffectTextureFormat.R16G16B16A16_FLOAT => Format.R16G16B16A16_Float,
                MagpieEffectTextureFormat.R16G16B16A16_UNORM => Format.R16G16B16A16_UNorm,
                MagpieEffectTextureFormat.R16G16B16A16_SNORM => Format.R16G16B16A16_SNorm,
                MagpieEffectTextureFormat.R32G32_FLOAT => Format.R32G32_Float,
                MagpieEffectTextureFormat.R10G10B10A2_UNORM => Format.R10G10B10A2_UNorm,
                MagpieEffectTextureFormat.R11G11B10_FLOAT => Format.R11G11B10_Float,
                MagpieEffectTextureFormat.R8G8B8A8_SNORM => Format.R8G8B8A8_SNorm,
                MagpieEffectTextureFormat.R16G16_FLOAT => Format.R16G16_Float,
                MagpieEffectTextureFormat.R16G16_UNORM => Format.R16G16_UNorm,
                MagpieEffectTextureFormat.R16G16_SNORM => Format.R16G16_SNorm,
                MagpieEffectTextureFormat.R32_FLOAT => Format.R32_Float,
                MagpieEffectTextureFormat.R8G8_UNORM => Format.R8G8_UNorm,
                MagpieEffectTextureFormat.R8G8_SNORM => Format.R8G8_SNorm,
                MagpieEffectTextureFormat.R16_FLOAT => Format.R16_Float,
                MagpieEffectTextureFormat.R16_UNORM => Format.R16_UNorm,
                MagpieEffectTextureFormat.R16_SNORM => Format.R16_SNorm,
                MagpieEffectTextureFormat.R8_UNORM => Format.R8_UNorm,
                MagpieEffectTextureFormat.R8_SNORM => Format.R8_SNorm,
                _ => Format.R8G8B8A8_UNorm,
            };
        }

        private void EnsureDevice()
        {
            if (_device is not null)
            {
                return;
            }
            try
            {
                _device = D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0]);
                _context = _device.ImmediateContext;
            }
            catch
            {
                _device = null;
                _context = null;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _context?.Dispose();
                _device?.Dispose();
                _context = null;
                _device = null;
                _disposed = true;
            }
        }
    }

    public sealed class ShaderRenderItem
    {
        public required MagpieEffectDesc Effect { get; init; }
        public required IReadOnlyDictionary<string, double> Parameters { get; init; }
        public int? OutputWidth { get; init; }
        public int? OutputHeight { get; init; }
    }
}

