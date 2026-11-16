using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace NeeView
{
    /// <summary>
    /// One entry in the resize shader chain.
    /// </summary>
    public partial class ShaderChainItem : ObservableObject
    {
        private string _shaderPath = "";
        private bool _isEnabled = true;
        private Dictionary<string, double> _parameters = new();

        public ShaderChainItem()
        {
        }

        public ShaderChainItem(string shaderPath)
        {
            _shaderPath = shaderPath;
        }

        /// <summary>
        /// Path relative to the effects folder, without the .hlsl extension.
        /// </summary>
        public string ShaderPath
        {
            get => _shaderPath;
            set => SetProperty(ref _shaderPath, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public Dictionary<string, double> Parameters
        {
            get => _parameters;
            set => SetProperty(ref _parameters, value ?? new());
        }

        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                var name = _shaderPath.Replace('\\', '/');
                var index = name.LastIndexOf('/');
                return index >= 0 ? name[(index + 1)..] : name;
            }
        }

        public double GetParameter(string name, double defaultValue)
        {
            return _parameters.TryGetValue(name, out var value) ? value : defaultValue;
        }

        public void SetParameter(string name, double value)
        {
            if (_parameters.TryGetValue(name, out var oldValue) && Math.Abs(oldValue - value) < double.Epsilon)
            {
                return;
            }
            _parameters[name] = value;
            OnPropertyChanged(nameof(Parameters));
        }

        public void ResetParameters()
        {
            _parameters.Clear();
            OnPropertyChanged(nameof(Parameters));
        }

        public ShaderChainItem Clone()
        {
            return new ShaderChainItem(_shaderPath)
            {
                IsEnabled = _isEnabled,
                Parameters = new Dictionary<string, double>(_parameters),
            };
        }
    }
}
