using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NeeView.Effects.Shaders
{
    /// <summary>
    /// Parses MagpieFX v4 metadata headers into a runtime descriptor.
    /// This parser deliberately does not execute shader code.
    /// </summary>
    public static class MagpieEffectParser
    {
        private const string MetaIndicator = "//!";
        private const int SupportedVersion = 4;

        private sealed class Block
        {
            public Block(string name, string body)
            {
                Name = name;
                Body = body;
            }

            public string Name { get; }
            public string Body { get; }
        }

        private static bool IsBlockMeta(string name)
        {
            return name is "PARAMETER" or "TEXTURE" or "SAMPLER" or "COMMON" or "PASS";
        }

        private static bool IsMetaDirective(string name)
        {
            return name is "PARAMETER" or "TEXTURE" or "SAMPLER" or "COMMON" or "PASS"
                or "LABEL" or "DEFAULT" or "MIN" or "MAX" or "STEP"
                or "FORMAT" or "SOURCE" or "WIDTH" or "HEIGHT" or "FILTER" or "ADDRESS"
                or "DESC" or "BLOCK_SIZE" or "NUM_THREADS" or "STYLE";
        }

        private static bool IsBlockStart(string name)
        {
            return name is "PARAMETER" or "TEXTURE" or "SAMPLER" or "COMMON" or "PASS";
        }

        private static (string Header, List<Block> Blocks) SplitSections(string source)
        {
            var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var header = new StringBuilder();
            var blocks = new List<Block>();
            var currentName = "";
            var currentBody = new StringBuilder();
            var inHeader = true;

            void Flush()
            {
                if (currentName == "")
                {
                    return;
                }
                blocks.Add(new Block(currentName, currentBody.ToString()));
                currentBody.Clear();
            }

            foreach (var line in lines)
            {
                var meta = GetMetaValue(line);
                if (IsBlockStart(meta.Name))
                {
                    if (inHeader)
                    {
                        inHeader = false;
                    }
                    else
                    {
                        Flush();
                    }
                    currentName = meta.Name;
                    currentBody.Clear();
                    currentBody.AppendLine(line);
                    continue;
                }

                if (inHeader)
                {
                    header.AppendLine(line);
                }
                else
                {
                    currentBody.AppendLine(line);
                }
            }

            Flush();
            return (header.ToString(), blocks);
        }

        public static MagpieEffectDesc Parse(string source, string sourcePath = "")
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new FormatException("MagpieFX source is empty.");
            }

            var (headerText, blocks) = SplitSections(source);
            if (!headerText.Contains("//!MAGPIE EFFECT", StringComparison.Ordinal))
            {
                throw new FormatException("Missing MagpieFX header.");
            }

            var desc = new MagpieEffectDesc
            {
                SourcePath = sourcePath,
                Name = string.IsNullOrWhiteSpace(sourcePath)
                    ? ""
                    : Path.GetFileNameWithoutExtension(sourcePath),
            };
            ParseHeader(headerText, desc);
            foreach (var block in blocks.Where(e => e.Name == "PARAMETER"))
            {
                desc.Parameters.Add(ParseParameter(block.Body));
            }

            desc.Textures.Add(new MagpieEffectTextureDesc
            {
                Name = "INPUT",
                Format = MagpieEffectTextureFormat.R8G8B8A8_UNORM,
            });
            desc.Textures.Add(new MagpieEffectTextureDesc
            {
                Name = "OUTPUT",
                Format = MagpieEffectTextureFormat.R8G8B8A8_UNORM,
            });

            foreach (var block in blocks.Where(e => e.Name == "TEXTURE"))
            {
                ParseTexture(block.Body, desc);
            }
            foreach (var block in blocks.Where(e => e.Name == "SAMPLER"))
            {
                ParseSampler(block.Body, desc);
            }
            desc.CommonSource = string.Join("\n", blocks.Where(e => e.Name == "COMMON").Select(e => SplitBodyAfterMeta(e.Body, "COMMON")));

            var passBlocks = blocks.Where(e => e.Name == "PASS").ToList();
            if (passBlocks.Count == 0)
            {
                throw new FormatException("MagpieFX source has no PASS block.");
            }

            var orderedPasses = passBlocks
                .Select(e => (Index: ParsePassIndex(e.Body), Body: e.Body))
                .OrderBy(e => e.Index)
                .ToList();
            if (orderedPasses.Select(e => e.Index).SequenceEqual(Enumerable.Range(1, orderedPasses.Count)) == false)
            {
                throw new FormatException("MagpieFX PASS indices must be consecutive and start at 1.");
            }

            for (var i = 0; i < orderedPasses.Count; i++)
            {
                var pass = ParsePass(orderedPasses[i].Body, i + 1, orderedPasses.Count, desc);
                pass.Source = SplitBodyAfterMeta(orderedPasses[i].Body, "PASS");
                desc.Passes.Add(pass);
            }

            return desc;
        }

        public static MagpieEffectDesc ParseFile(string path)
        {
            var source = File.ReadAllText(path);
            return Parse(source, path);
        }

        private static List<Block> SplitBlocks(string source)
        {
            var result = new List<Block>();
            var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var currentName = "HEADER";
            var current = new StringBuilder();

            foreach (var line in lines)
            {
                if (line.StartsWith("//!", StringComparison.Ordinal))
                {
                    var rest = line[3..];
                    var meta = GetMetaValue(line);
                    if (meta.Name == "MAGPIE")
                    {
                        if (current.Length > 0)
                        {
                            result.Add(new Block(currentName, current.ToString()));
                        }
                        current.Clear();
                        currentName = "HEADER";
                    }
                    else if (meta.Name is "PARAMETER" or "TEXTURE" or "SAMPLER" or "COMMON" or "PASS")
                    {
                        if (current.Length > 0)
                        {
                            result.Add(new Block(currentName, current.ToString()));
                        }
                        current.Clear();
                        currentName = meta.Name;
                    }
                }

                current.AppendLine(line);
            }

            if (current.Length > 0)
            {
                result.Add(new Block(currentName, current.ToString()));
            }
            return result;
        }

        private static void ParseHeader(string body, MagpieEffectDesc desc)
        {
            var lines = body.Split('\n');
            var versionFound = false;
            foreach (var line in lines)
            {
                var value = GetMetaValue(line);
                if (value.Name == "VERSION")
                {
                    if (!int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) ||
                        version != SupportedVersion)
                    {
                        throw new FormatException("Only MagpieFX version 4 is supported.");
                    }
                    versionFound = true;
                }
                else if (value.Name == "SORT_NAME")
                {
                    desc.SortName = value.Value;
                }
                else if (value.Name == "USE")
                {
                    desc.UsesMulAdd = value.Value.Split(',').Any(e => e.Trim().Equals("MulAdd", StringComparison.OrdinalIgnoreCase));
                    desc.UsesDynamic = value.Value.Split(',').Any(e => e.Trim().Equals("_Dynamic", StringComparison.OrdinalIgnoreCase));
                }
                else if (value.Name == "CAPABILITY")
                {
                    desc.SupportsFp16 = value.Value.Split(',').Any(e => e.Trim().Equals("FP16", StringComparison.OrdinalIgnoreCase));
                }
            }

            if (!versionFound)
            {
                throw new FormatException("MagpieFX version is missing.");
            }
        }

        private static MagpieParameterDesc ParseParameter(string body)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in body.Split('\n'))
            {
                var meta = GetMetaValue(line);
                if (!string.IsNullOrEmpty(meta.Name))
                {
                    values[meta.Name] = meta.Value;
                }
            }

            var declaration = body.Split('\n')
                .Select(e => e.Trim())
                .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e) && !e.StartsWith("//!", StringComparison.Ordinal));
            if (declaration is null)
            {
                throw new FormatException("Parameter declaration is missing.");
            }

            var tokens = declaration.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2 || (tokens[0] != "float" && tokens[0] != "int"))
            {
                throw new FormatException($"Invalid parameter declaration: {declaration}");
            }

            var name = tokens[1].TrimEnd(';');
            return new MagpieParameterDesc
            {
                Name = name,
                Label = values.GetValueOrDefault("LABEL", name),
                IsInteger = tokens[0] == "int",
                DefaultValue = ParseNumber(values, "DEFAULT"),
                Minimum = ParseNumber(values, "MIN"),
                Maximum = ParseNumber(values, "MAX"),
                Step = ParseNumber(values, "STEP"),
            };
        }

        private static void ParseTexture(string body, MagpieEffectDesc desc)
        {
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in body.Split('\n'))
            {
                var value = GetMetaValue(line);
                if (!string.IsNullOrEmpty(value.Name))
                {
                    meta[value.Name] = value.Value;
                }
            }

            var declaration = body.Split('\n')
                .Select(e => e.Trim())
                .FirstOrDefault(e => e.StartsWith("Texture2D", StringComparison.Ordinal));
            if (declaration is null)
            {
                throw new FormatException("Texture declaration is missing.");
            }

            var name = declaration["Texture2D".Length..].Trim().TrimEnd(';');
            if (name == "INPUT")
            {
                return;
            }

            var formatName = meta.GetValueOrDefault("FORMAT", "");
            var format = Enum.TryParse<MagpieEffectTextureFormat>(formatName, true, out var parsed)
                ? parsed
                : MagpieEffectTextureFormat.Unknown;

            var texture = new MagpieEffectTextureDesc
            {
                Name = name,
                WidthExpression = meta.GetValueOrDefault("WIDTH", "").Replace(" ", ""),
                HeightExpression = meta.GetValueOrDefault("HEIGHT", "").Replace(" ", ""),
                Format = format,
                Source = meta.GetValueOrDefault("SOURCE", ""),
            };

            if (name == "OUTPUT")
            {
                var output = desc.Textures[1];
                output.WidthExpression = texture.WidthExpression;
                output.HeightExpression = texture.HeightExpression;
                output.Format = format;
            }
            else
            {
                if (texture.Format == MagpieEffectTextureFormat.Unknown && string.IsNullOrEmpty(texture.Source))
                {
                    throw new FormatException($"Intermediate texture {name} has no FORMAT.");
                }
                desc.Textures.Add(texture);
            }
        }

        private static void ParseSampler(string body, MagpieEffectDesc desc)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in body.Split('\n'))
            {
                var meta = GetMetaValue(line);
                if (!string.IsNullOrEmpty(meta.Name))
                {
                    values[meta.Name] = meta.Value;
                }
            }

            var declaration = body.Split('\n')
                .Select(e => e.Trim())
                .FirstOrDefault(e => e.StartsWith("SamplerState", StringComparison.Ordinal));
            if (declaration is null)
            {
                throw new FormatException("Sampler declaration is missing.");
            }

            var name = declaration["SamplerState".Length..].Trim().TrimEnd(';');
            var filter = values.GetValueOrDefault("FILTER", "LINEAR");
            var address = values.GetValueOrDefault("ADDRESS", "CLAMP");
            desc.Samplers.Add(new MagpieSamplerDesc
            {
                Name = name,
                Filter = filter.Equals("POINT", StringComparison.OrdinalIgnoreCase) ? MagpieSamplerFilter.Point : MagpieSamplerFilter.Linear,
                Address = address.Equals("WRAP", StringComparison.OrdinalIgnoreCase) ? MagpieSamplerAddress.Wrap : MagpieSamplerAddress.Clamp,
            });
        }

        private static int ParsePassIndex(string body)
        {
            foreach (var line in body.Split('\n'))
            {
                var value = GetMetaValue(line);
                if (value.Name == "PASS" && int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                {
                    return index;
                }
            }
            throw new FormatException("PASS index is missing.");
        }

        private static MagpiePassDesc ParsePass(string body, int index, int count, MagpieEffectDesc desc)
        {
            var pass = new MagpiePassDesc { Index = index };
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in body.Split('\n'))
            {
                var meta = GetMetaValue(line);
                if (!string.IsNullOrEmpty(meta.Name))
                {
                    values[meta.Name] = meta.Value;
                }
            }

            if (!values.TryGetValue("IN", out var inputText) || !values.TryGetValue("OUT", out var outputText))
            {
                throw new FormatException($"PASS {index} must define IN and OUT.");
            }

            foreach (var name in inputText.Split(',').Select(e => e.Trim()))
            {
                var texture = desc.Textures.FirstOrDefault(e => e.Name == name);
                if (texture is null || texture.Name == "OUTPUT")
                {
                    throw new FormatException($"PASS {index} references unknown input texture {name}.");
                }
                pass.Inputs.Add(desc.Textures.IndexOf(texture));
            }

            if (index == count)
            {
                if (outputText.Trim() != "OUTPUT")
                {
                    throw new FormatException($"PASS {index} must output OUTPUT.");
                }
                pass.Outputs.Add(1);
            }
            else
            {
                foreach (var name in outputText.Split(',').Select(e => e.Trim()))
                {
                    var texture = desc.Textures.FirstOrDefault(e => e.Name == name);
                    if (texture is null || texture.Name is "INPUT" or "OUTPUT")
                    {
                        throw new FormatException($"PASS {index} references invalid output texture {name}.");
                    }
                    pass.Outputs.Add(desc.Textures.IndexOf(texture));
                }
            }

            if (values.TryGetValue("STYLE", out var style))
            {
                if (!style.Equals("PS", StringComparison.OrdinalIgnoreCase) && !style.Equals("CS", StringComparison.OrdinalIgnoreCase))
                {
                    throw new FormatException($"PASS {index} has invalid STYLE.");
                }
                pass.IsPixelStyle = style.Equals("PS", StringComparison.OrdinalIgnoreCase);
                if (pass.IsPixelStyle)
                {
                    pass.NumThreads = [64, 1, 1];
                    pass.BlockWidth = 16;
                    pass.BlockHeight = 16;
                }
            }

            if (values.TryGetValue("DESC", out var description))
            {
                pass.Description = description;
            }
            if (values.TryGetValue("BLOCK_SIZE", out var blockSize))
            {
                var parts = blockSize.Split(',').Select(e => e.Trim()).ToArray();
                pass.BlockWidth = int.Parse(parts[0], CultureInfo.InvariantCulture);
                pass.BlockHeight = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : pass.BlockWidth;
            }
            if (values.TryGetValue("NUM_THREADS", out var numThreads))
            {
                var parts = numThreads.Split(',').Select(e => e.Trim()).ToArray();
                for (var i = 0; i < 3; i++)
                {
                    pass.NumThreads[i] = i < parts.Length ? int.Parse(parts[i], CultureInfo.InvariantCulture) : 1;
                }
            }

            if (!pass.IsPixelStyle && (pass.BlockWidth <= 0 || pass.BlockHeight <= 0 || pass.NumThreads.Any(e => e <= 0)))
            {
                throw new FormatException($"PASS {index} has invalid BLOCK_SIZE or NUM_THREADS.");
            }

            if (!pass.IsPixelStyle && !values.ContainsKey("BLOCK_SIZE") && !values.ContainsKey("NUM_THREADS"))
            {
                throw new FormatException($"PASS {index} must define BLOCK_SIZE and NUM_THREADS.");
            }

            return pass;
        }

        private static (string Name, string Value) GetMetaValue(string line)
        {
            var text = line.TrimStart();
            if (!text.StartsWith("//!", StringComparison.Ordinal))
            {
                return ("", "");
            }
            var content = text[3..].Trim();
            var index = content.IndexOf(' ');
            return index < 0 ? (content, "") : (content[..index], content[(index + 1)..].Trim());
        }

        private static string SplitBodyAfterMeta(string body, string blockName)
        {
            var lines = body.Split('\n');
            var foundHeader = false;
            var builder = new StringBuilder();
            foreach (var line in lines)
            {
                var text = line.TrimStart();
                if (!text.StartsWith("//!", StringComparison.Ordinal))
                {
                    if (foundHeader)
                    {
                        builder.AppendLine(line);
                    }
                    continue;
                }

                var meta = GetMetaValue(line);
                if (meta.Name == blockName)
                {
                    foundHeader = true;
                }
            }
            return builder.ToString();
        }

        private static double ParseNumber(Dictionary<string, string> values, string name)
        {
            if (!values.TryGetValue(name, out var text) ||
                !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"Missing or invalid numeric metadata: {name}");
            }
            return value;
        }
    }
}
