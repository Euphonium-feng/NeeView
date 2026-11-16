using CommunityToolkit.Mvvm.ComponentModel;
using Generator.Equals;
using NeeView.Windows.Property;
using PhotoSauce.MagicScaler;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace NeeView
{
    /// <summary>
    /// Resize filter (PhotoSauce.MagicScaler)
    /// </summary>
    [Equatable(Explicit = true, IgnoreInheritedMembers = true)]
    public partial class ImageResizeFilterConfig : ObservableObject
    {
        [DefaultEquality] private bool _isResizeFilterEnabled = false;
        [DefaultEquality] private ShaderChainConfig _shaders = new();

        public ImageResizeFilterConfig()
        {
            _shaders.PropertyChanged += Shaders_PropertyChanged;
        }

        [PropertyMember]
        public bool IsEnabled
        {
            get { return _isResizeFilterEnabled; }
            set { SetProperty(ref _isResizeFilterEnabled, value); }
        }

        [PropertyMember]
        public ShaderChainConfig Shaders
        {
            get => _shaders;
            set
            {
                if (_shaders != value)
                {
                    _shaders.PropertyChanged -= Shaders_PropertyChanged;
                    _shaders = value ?? new();
                    _shaders.PropertyChanged += Shaders_PropertyChanged;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Legacy value, read only for migration.
        /// </summary>
        [Obsolete("Use Shaders instead.")]
        [JsonPropertyName("ResizeInterpolation")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
        public ResizeInterpolation? LegacyResizeInterpolation { get; set; }

        /// <summary>
        /// Legacy value, read only for migration.
        /// </summary>
        [Obsolete("Use Shaders instead.")]
        [JsonPropertyName("IsUnsharpMaskEnabled")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
        public bool? LegacyIsUnsharpMaskEnabled { get; set; }

        /// <summary>
        /// Legacy value, read only for migration.
        /// </summary>
        [Obsolete("Use Shaders instead.")]
        [JsonPropertyName("UnsharpMask")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
        public UnsharpMaskConfig? LegacyUnsharpMask { get; set; }

        public bool IsLegacyMigrationRequired => LegacyResizeInterpolation.HasValue;

        public ShaderChainConfig MigrateLegacy()
        {
            if (!LegacyResizeInterpolation.HasValue)
            {
                return Shaders;
            }

            var shader = LegacyResizeInterpolation.Value switch
            {
                ResizeInterpolation.NearestNeighbor => "Common/Nearest",
                ResizeInterpolation.Average => "Common/Bilinear",
                ResizeInterpolation.Linear => "Common/Bilinear",
                ResizeInterpolation.Quadratic => "Common/Bicubic",
                ResizeInterpolation.Hermite => "Common/Bicubic",
                ResizeInterpolation.Mitchell => "Common/Bicubic",
                ResizeInterpolation.CatmullRom => "Common/Bicubic",
                ResizeInterpolation.Cubic => "Common/Bicubic",
                ResizeInterpolation.CubicSmoother => "Common/Bicubic",
                ResizeInterpolation.Lanczos => "Common/Lanczos",
                ResizeInterpolation.Spline36 => "Common/Jinc",
                _ => "Common/Lanczos",
            };

            var chain = new ShaderChainConfig();
            chain.Items.Clear();
            chain.Items.Add(new ShaderChainItem(shader));
            Shaders = chain;
            LegacyResizeInterpolation = null;
            LegacyIsUnsharpMaskEnabled = null;
            LegacyUnsharpMask = null;
            return chain;
        }

        private void Shaders_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Shaders));
        }

        public int GetEnvironmentHashCode()
        {
            var hash = new HashCode();
            hash.Add(_isResizeFilterEnabled);
            foreach (var item in _shaders.Items)
            {
                hash.Add(item.ShaderPath);
                hash.Add(item.IsEnabled);
                foreach (var parameter in item.Parameters.OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    hash.Add(parameter.Key);
                    hash.Add(parameter.Value);
                }
            }
            return hash.ToHashCode();
        }

        public ProcessImageSettings CreateProcessImageSetting()
        {
            var setting = new ProcessImageSettings();

            setting.Interpolation = InterpolationSettings.Lanczos;

            return setting;
        }
    }
}
