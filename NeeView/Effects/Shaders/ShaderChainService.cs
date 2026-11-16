using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NeeView.Effects.Shaders
{
    /// <summary>
    /// Process-wide shader catalog and compiled-chain cache.
    /// </summary>
    public static class ShaderChainService
    {
        private static readonly object _lock = new();
        private static ShaderEffectCatalog? _catalog;
        private static MagpieEffectCompiler? _compiler;
        private static ShaderChainRenderer? _renderer;
        private static string? _lastFallbackReason;

        public static string EffectsRootPath
        {
            get
            {
                var path = Path.Combine(AppContext.BaseDirectory, "effects");
                return Directory.Exists(path) ? path : Path.Combine(System.Environment.CurrentDirectory, "effects");
            }
        }

        public static ShaderEffectCatalog Catalog
        {
            get
            {
                lock (_lock)
                {
                    return _catalog ??= new ShaderEffectCatalog(EffectsRootPath);
                }
            }
        }

        public static string? LastFallbackReason
        {
            get
            {
                lock (_lock) return _lastFallbackReason;
            }
        }

        public static void ClearFallbackReason()
        {
            lock (_lock) _lastFallbackReason = null;
        }

        public static BitmapSource Render(BitmapSource source, Size targetSize)
        {
            var config = Config.Current.ImageResizeFilter;
            var chain = BuildChain(config);
            if (chain.Count == 0)
            {
                return source;
            }

            try
            {
                var renderer = GetRenderer();
                var result = renderer.Render(source, chain, targetSize);
                lock (_lock) _lastFallbackReason = null;
                return result;
            }
            catch (Exception ex)
            {
                lock (_lock) _lastFallbackReason = ex.Message;
                return CreateLanczosFallback(source, targetSize);
            }
        }

        public static IReadOnlyList<ShaderRenderItem> BuildChain(ImageResizeFilterConfig config)
        {
            var result = new List<ShaderRenderItem>();
            var catalog = Catalog;
            var items = config.Shaders.Items.Where(e => e.IsEnabled).ToList();
            foreach (var item in items)
            {
                var info = catalog.Effects.FirstOrDefault(e => string.Equals(e.RelativePath, item.ShaderPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
                if (info is null)
                {
                    continue;
                }

                var desc = GetCompiled(info);
                result.Add(new ShaderRenderItem
                {
                    Effect = desc,
                    Parameters = item.Parameters,
                });
            }
            return result;
        }

        private static MagpieEffectDesc GetCompiled(ShaderEffectInfo info)
        {
            lock (_lock)
            {
                _compiler ??= new MagpieEffectCompiler(Path.Combine(Environment.DefaultTempPath, "NeeView", "ShaderCache"), EffectsRootPath);
                var desc = MagpieEffectParser.ParseFile(info.FullPath);
                return _compiler.Compile(desc, new MagpieCompileOptions());
            }
        }

        private static ShaderChainRenderer GetRenderer()
        {
            lock (_lock)
            {
                _renderer ??= new ShaderChainRenderer();
                return _renderer;
            }
        }

        private static BitmapSource CreateLanczosFallback(BitmapSource source, Size targetSize)
        {
            var width = Math.Max(1, (int)Math.Round(targetSize.Width));
            var height = Math.Max(1, (int)Math.Round(targetSize.Height));
            var scaled = new System.Windows.Media.Imaging.TransformedBitmap(
                source,
                new System.Windows.Media.ScaleTransform((double)width / source.PixelWidth, (double)height / source.PixelHeight));
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(scaled));
            using var stream = new System.IO.MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;
            var decoded = new System.Windows.Media.Imaging.BitmapImage();
            decoded.BeginInit();
            decoded.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            decoded.StreamSource = stream;
            decoded.EndInit();
            decoded.Freeze();
            return decoded;
        }
    }
}
