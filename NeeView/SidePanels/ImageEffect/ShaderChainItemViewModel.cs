using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeeView.Effects.Shaders;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace NeeView
{
    public partial class ShaderChainItemViewModel : ObservableObject
    {
        private readonly ShaderChainItem _item;
        private readonly Action<ShaderChainItemViewModel> _remove;

        public ShaderChainItemViewModel(ShaderChainItem item, ShaderParameterViewModel? parameters, Action<ShaderChainItemViewModel> remove)
        {
            _item = item;
            _remove = remove;
            Parameters = parameters;
            ParameterItems = parameters?.Items ?? new();
        }

        public ShaderChainItem Model => _item;
        public string Name => ShaderEffectCatalog.GetDisplayName(_item.ShaderPath);
        public string ShaderPath => _item.ShaderPath;
        public string DragFormat => "NeeView.ShaderChainItem";
        public string Description => _item.ShaderPath;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayNumber))]
        public partial int Number { get; set; }

        public string DisplayNumber => Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public bool IsEnabled
        {
            get => _item.IsEnabled;
            set => _item.IsEnabled = value;
        }

        public ShaderParameterViewModel? Parameters { get; }
        public ObservableCollection<ShaderParameterItemViewModel> ParameterItems { get; }
        public bool HasParameters => ParameterItems.Count > 0;

        [RelayCommand]
        private void Remove() => _remove(this);

        [RelayCommand]
        private void ResetParameters()
        {
            Parameters?.Reset();
        }
    }

    public sealed class ShaderParameterViewModel
    {
        private readonly ShaderChainItem _item;
        private readonly MagpieEffectDesc _effect;

        public ShaderParameterViewModel(ShaderChainItem item, MagpieEffectDesc effect)
        {
            _item = item;
            _effect = effect;
            Items = new ObservableCollection<ShaderParameterItemViewModel>(
                effect.Parameters.Select(p => new ShaderParameterItemViewModel(item, p)));
        }

        public ObservableCollection<ShaderParameterItemViewModel> Items { get; }

        public void Reset()
        {
            _item.ResetParameters();
            foreach (var item in Items)
            {
                item.Refresh();
            }
        }
    }

    public sealed class ShaderParameterItemViewModel : ObservableObject
    {
        private readonly ShaderChainItem _item;
        private readonly MagpieParameterDesc _parameter;

        public ShaderParameterItemViewModel(ShaderChainItem item, MagpieParameterDesc parameter)
        {
            _item = item;
            _parameter = parameter;
        }

        public string Label => string.IsNullOrWhiteSpace(_parameter.Label) ? _parameter.Name : _parameter.Label;
        public double Minimum => _parameter.Minimum;
        public double Maximum => _parameter.Maximum;
        public double Step => _parameter.Step <= 0 ? 0.01 : _parameter.Step;

        public double Value
        {
            get => _item.GetParameter(_parameter.Name, _parameter.DefaultValue);
            set
            {
                _item.SetParameter(_parameter.Name, value);
                OnPropertyChanged();
            }
        }

        public void Refresh() => OnPropertyChanged(nameof(Value));
    }
}
