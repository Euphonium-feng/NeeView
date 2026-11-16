using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace NeeView
{
    /// <summary>
    /// Ordered shader chain. Items run from top to bottom.
    /// </summary>
    public partial class ShaderChainConfig : ObservableObject
    {
        public const string DefaultShaderPath = "Common/Lanczos";

        private ObservableCollection<ShaderChainItem> _items = new();

        public ShaderChainConfig()
        {
            _items.Add(new ShaderChainItem(DefaultShaderPath));
        }

        public ObservableCollection<ShaderChainItem> Items
        {
            get => _items;
            set => SetProperty(ref _items, value ?? new());
        }

        public bool IsEmpty => _items.Count == 0;
    }
}
