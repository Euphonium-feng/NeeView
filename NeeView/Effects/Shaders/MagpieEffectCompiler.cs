using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Vortice.D3DCompiler;
using Vortice.Direct3D;

namespace NeeView.Effects.Shaders
{
    public sealed class MagpieCompileOptions
    {
        public bool EnableFp16 { get; set; }
        public bool WarningsAreErrors { get; set; }
    }

    /// <summary>
    /// Compiles MagpieFX sources to DXBC compute shader blobs and caches them by content hash.
    /// </summary>
    public sealed class MagpieEffectCompiler
    {
        // Bump when generated HLSL or the constant layout changes.
        private const string CompilerVersion = "2";
        private readonly string _cacheDirectory;
        private readonly string? _effectsRoot;

        public MagpieEffectCompiler(string cacheDirectory, string? effectsRoot = null)
        {
            _cacheDirectory = cacheDirectory;
            _effectsRoot = effectsRoot;
        }

        public MagpieEffectDesc Compile(MagpieEffectDesc desc, MagpieCompileOptions? options = null)
        {
            options ??= new MagpieCompileOptions();
            foreach (var pass in desc.Passes)
            {
                pass.Bytecode = CompilePass(desc, pass.Index, options);
            }
            return desc;
        }

        private byte[] CompilePass(MagpieEffectDesc desc, int passIndex, MagpieCompileOptions options)
        {
            var macros = MagpieEffectSourceBuilder.BuildMacros(desc, passIndex, options.EnableFp16);
            var source = MagpieEffectSourceBuilder.BuildPassSource(desc, passIndex);
            source = ExpandIncludes(source, Path.GetDirectoryName(desc.SourcePath) ?? "", _effectsRoot, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            var key = BuildCacheKey(desc, passIndex, options, macros);
            var cachePath = Path.Combine(_cacheDirectory, key + ".cso");

            if (File.Exists(cachePath))
            {
                var cached = File.ReadAllBytes(cachePath);
                if (cached.Length > 0)
                {
                    return cached;
                }
            }

            try
            {
                var defines = macros
                    .Select(e => new ShaderMacro(e.Key, e.Value))
                    .ToArray();
                var flags = ShaderFlags.EnableStrictness;
                if (options.WarningsAreErrors)
                {
                    flags |= ShaderFlags.WarningsAreErrors;
                }

                var bytecode = Compiler.Compile(
                    RemoveComments(source),
                    defines,
                    null!,
                    "__M",
                    $"{desc.Name}_Pass{passIndex}.hlsl",
                    "cs_5_0",
                    flags,
                    EffectFlags.None
                );

                if (bytecode.IsEmpty)
                {
                    throw new InvalidOperationException("D3DCompile returned an empty shader blob.");
                }

                var bytes = bytecode.ToArray();
                Directory.CreateDirectory(_cacheDirectory);
                File.WriteAllBytes(cachePath, bytes);
                return bytes;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to compile MagpieFX {desc.Name} pass {passIndex}: {ex.Message}", ex);
            }
        }

        private static string BuildCacheKey(MagpieEffectDesc desc, int passIndex, MagpieCompileOptions options, IReadOnlyDictionary<string, string> macros)
        {
            var builder = new StringBuilder();
            builder.Append(CompilerVersion).Append('|').Append(desc.Name).Append('|').Append(passIndex).Append('|');
            builder.Append(options.EnableFp16 ? '1' : '0').Append('|').Append(desc.SourcePath).Append('|');
            foreach (var macro in macros.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                builder.Append(macro.Key).Append('=').Append(macro.Value).Append(';');
            }
            builder.Append('|').Append(desc.Passes[passIndex - 1].Source);
            builder.Append('|').Append(desc.CommonSource);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
            return Convert.ToHexString(hash);
        }

        /// <summary>
        /// Remove ordinary comments before D3DCompile. The MagpieFX metadata comments
        /// are removed separately by the parser and the remaining Chinese comments
        /// can otherwise confuse the legacy compiler's source handling.
        /// </summary>
        private static string RemoveComments(string source)
        {
            var result = new StringBuilder(source.Length);
            var inBlockComment = false;
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                var next = i + 1 < source.Length ? source[i + 1] : '\0';
                if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                    continue;
                }
                if (c == '/' && next == '/')
                {
                    if (i + 2 >= source.Length || source[i + 2] != '!')
                    {
                        while (i < source.Length && source[i] != '\n')
                        {
                            i++;
                        }
                        result.Append('\n');
                        continue;
                    }
                }
                if (c == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
                result.Append(c);
            }
            return result.ToString();
        }

        private static string ExpandIncludes(string source, string baseDirectory, string? effectsRoot, HashSet<string> visited)
        {
            var lines = source.Replace("\r\n", "\n").Split('\n');
            var result = new StringBuilder(source.Length);
            foreach (var line in lines)
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, @"^\s*#include\s+""([^""]+)""");
                if (!match.Success)
                {
                    result.AppendLine(line);
                    continue;
                }

                var relative = match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                var path = ResolveIncludePath(relative, baseDirectory, effectsRoot);
                if (!File.Exists(path))
                {
                    var fileName = Path.GetFileName(relative);
                    path = FindIncludedFile(fileName, effectsRoot);
                    if (path is null)
                    {
                        throw new FileNotFoundException($"Shader include not found: {match.Groups[1].Value}", Path.Combine(baseDirectory, relative));
                    }
                }
                if (!visited.Add(path))
                {
                    continue;
                }
                var nested = File.ReadAllText(path);
                result.AppendLine(ExpandIncludes(nested, Path.GetDirectoryName(path) ?? baseDirectory, effectsRoot, visited));
            }
            return result.ToString();
        }

        private static string ResolveIncludePath(string relative, string baseDirectory, string? effectsRoot)
        {
            var path = Path.Combine(baseDirectory, relative);
            if (File.Exists(path))
            {
                return path;
            }

            var normalized = relative.Replace('\\', '/');
            while (normalized.StartsWith("../", StringComparison.Ordinal) && !string.IsNullOrEmpty(effectsRoot))
            {
                normalized = normalized[3..];
                var candidate = Path.Combine(effectsRoot, normalized);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return path;
        }

        private static string? FindIncludedFile(string fileName, string? effectsRoot)
        {
            if (string.IsNullOrEmpty(effectsRoot) || !Directory.Exists(effectsRoot))
            {
                return null;
            }
            return Directory.EnumerateFiles(effectsRoot, fileName, SearchOption.AllDirectories).FirstOrDefault();
        }

        private sealed class IncludeHandler : Include
        {
            private readonly string _baseDirectory;
            private readonly string? _effectsRoot;

            public IncludeHandler(string baseDirectory, string? effectsRoot)
            {
                _baseDirectory = baseDirectory;
                _effectsRoot = effectsRoot;
            }

            public Stream Open(IncludeType includeType, string fileName, Stream? parentStream)
            {
                var relative = fileName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                var path = Path.Combine(_baseDirectory, relative);
                if (!File.Exists(path) && !string.IsNullOrEmpty(_effectsRoot))
                {
                    path = Path.Combine(_effectsRoot, relative);
                }
                return File.OpenRead(path);
            }

            public void Close(Stream stream)
            {
                stream.Dispose();
            }

            public void Dispose()
            {
            }
        }
    }
}

