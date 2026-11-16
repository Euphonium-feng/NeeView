using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeeLaboratory.ComponentModel;
using NeeView.Collections.ObjectModel;
using NeeView.Effects;
using NeeView.Effects.Shaders;
using NeeView.Properties;
using NeeView.Windows.Property;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;

namespace NeeView
{
    /// <summary>
    /// ImageEffect : ViewModel
    /// </summary>
    public partial class ImageEffectViewModel : ObservableObject
    {
        private readonly EffectProfileCollection _profileSources;
        private readonly System.Collections.ObjectModel.ObservableCollection<ShaderChainItemViewModel> _shaderChain = new();

        public ImageEffectViewModel()
        {
            this.CustomSizeProfile = new PropertyDocument(Config.Current.ImageCustomSize);
            this.CustomSizeProfile.SetVisualType<PropertyValue_Boolean>(PropertyVisualType.ToggleSwitch);

            this.TrimProfile = new PropertyDocument(Config.Current.ImageTrim);
            this.TrimProfile.SetVisualType<PropertyValue_Boolean>(PropertyVisualType.ToggleSwitch);

            this.GridLineProfile = new PropertyDocument(Config.Current.ImageGrid);
            this.GridLineProfile.SetVisualType<PropertyValue_Boolean>(PropertyVisualType.ToggleSwitch);

            EffectLayers = new(Config.Current.ImageEffect.Layers, e => new EffectLayerDocument(e));
            EffectLayers.CollectionChanged += (s, e) => UpdateCanExecute();
            ShaderEffects = new(new ShaderEffectCatalog(ShaderChainService.EffectsRootPath).Effects);
            ReloadShaderChain();

            _profileSources = EffectProfileCollection.Current;
            _profileSources.SubscribePropertyChanged(nameof(EffectProfileCollection.SelectedProfile),
                (s, e) =>
                {
                    OnPropertyChanged(nameof(SelectedProfile));
                    UpdateCanExecute();
                });

            MoreMenuDescription = new ImageEffectMoreMenuDescription(this);

            UpdateCanExecute();
        }


        public event EventHandler? RenameProfileRequested;
        public event EventHandler<DeleteConfirmEventArgs>? DeleteConfirmRequested;


        public ObservableCollection<EffectProfile> Profiles => _profileSources.Profiles;

        public EffectProfile? SelectedProfile
        {
            get => _profileSources.SelectedProfile;
            set => _profileSources.SelectedProfile = value;
        }

        public PropertyDocument CustomSizeProfile { get; set; }

        public PropertyDocument TrimProfile { get; set; }

        public PropertyDocument GridLineProfile { get; set; }

        public Dictionary<EffectType, string> EffectTypeList { get; } = AliasNameExtensions.GetAliasNameDictionary<EffectType>();

        public ObservableCollectionSync<EffectLayer, EffectLayerDocument> EffectLayers { get; set; }

        public System.Collections.ObjectModel.ObservableCollection<ShaderChainItemViewModel> ShaderChain => _shaderChain;

        public System.Collections.ObjectModel.ObservableCollection<ShaderEffectInfo> ShaderEffects { get; }

        [ObservableProperty]
        public partial string? SelectedShaderPath { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasShaderParameters))]
        public partial ShaderChainItemViewModel? SelectedShaderChainItem { get; set; }

        public bool HasShaderParameters => SelectedShaderChainItem?.HasParameters == true;

        [RelayCommand]
        public void AddShader(string? shaderPath)
        {
            if (string.IsNullOrWhiteSpace(shaderPath)) return;
            Config.Current.ImageResizeFilter.Shaders.Items.Add(new ShaderChainItem(shaderPath));
            ReloadShaderChain();
        }

        [RelayCommand]
        public void RemoveShader(ShaderChainItemViewModel? item)
        {
            if (item is null) return;
            Config.Current.ImageResizeFilter.Shaders.Items.Remove(item.Model);
            ReloadShaderChain();
        }

        [RelayCommand]
        public void ResetShaderParameters(ShaderChainItemViewModel? item)
        {
            item?.Parameters?.Reset();
        }

        public void ReloadShaderChain()
        {
            _shaderChain.Clear();
            var catalog = new ShaderEffectCatalog(ShaderChainService.EffectsRootPath);
            foreach (var item in Config.Current.ImageResizeFilter.Shaders.Items)
            {
                var info = catalog.Effects.FirstOrDefault(e => string.Equals(e.RelativePath, item.ShaderPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
                if (info is null)
                {
                    _shaderChain.Add(new ShaderChainItemViewModel(item, null, RemoveShader) { Number = _shaderChain.Count + 1 });
                    continue;
                }
                var desc = MagpieEffectParser.ParseFile(info.FullPath);
                _shaderChain.Add(new ShaderChainItemViewModel(item, new ShaderParameterViewModel(item, desc), RemoveShader) { Number = _shaderChain.Count + 1 });
            }
            OnPropertyChanged(nameof(HasShaderParameters));
        }


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AddEffectLayerCommand))]
        public partial bool CanAddEffectLayer { get; private set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
        public partial bool CanDeleteProfile { get; private set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RenameProfileCommand))]
        public partial bool CanRenameProfile { get; private set; }

        private void UpdateCanExecute()
        {
            CanAddEffectLayer = Config.Current.ImageEffect.Layers.CanCreateNew();
            CanDeleteProfile = _profileSources.CanDelete(SelectedProfile);
            CanRenameProfile = _profileSources.CanRename(SelectedProfile);
        }

        public void ResetValue()
        {
            var chain = Config.Current.ImageResizeFilter.Shaders;
            chain.Items.Clear();
            chain.Items.Add(new ShaderChainItem(ShaderChainConfig.DefaultShaderPath));
            ReloadShaderChain();
        }

        [RelayCommand(CanExecute = nameof(CanAddEffectLayer))]
        public void AddEffectLayer()
        {
            Config.Current.ImageEffect.Layers.CreateNew();
        }

        [RelayCommand]
        public void CreateNewProfile()
        {
            _profileSources.CreateNew();
        }

        [RelayCommand]
        public void CloneProfile()
        {
            _profileSources.Clone();
        }

        [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
        public void DeleteProfile()
        {
            var args = new DeleteConfirmEventArgs(TextResources.GetString("EffectProfileDeleteDialog.Title"), TextResources.GetFormatString("DeleteDialog.Message", SelectedProfile?.Name));
            DeleteConfirmRequested?.Invoke(this, args);

            if (args.DialogResult == true)
            {
                _profileSources.Delete(SelectedProfile);
            }
        }


        [RelayCommand(CanExecute = nameof(CanRenameProfile))]
        public void RenameProfile()
        {
            RenameProfileRequested?.Invoke(this, EventArgs.Empty);
        }

        public bool RenameProfile(string name)
        {
            if (SelectedProfile is null) return false;
            if (string.IsNullOrEmpty(name)) return false;

            _profileSources.Rename(SelectedProfile, name);
            return true;
        }


        #region MoreMenu

        public ImageEffectMoreMenuDescription MoreMenuDescription { get; }

        public class ImageEffectMoreMenuDescription : ItemsListMoreMenuDescription
        {
            private readonly ImageEffectViewModel _vm;

            public ImageEffectMoreMenuDescription(ImageEffectViewModel vm)
            {
                _vm = vm;
            }

            public override ContextMenu Create()
            {
                var menu = new ContextMenu();
                menu.Items.Add(CreateCommandMenuItem(TextResources.GetString("Menu.New"), _vm.CreateNewProfileCommand));
                menu.Items.Add(CreateCommandMenuItem(TextResources.GetString("Menu.Clone"), _vm.CloneProfileCommand));
                menu.Items.Add(CreateCommandMenuItem(TextResources.GetString("Menu.Delete"), _vm.DeleteProfileCommand));
                menu.Items.Add(CreateCommandMenuItem(TextResources.GetString("Menu.Rename"), _vm.RenameProfileCommand));
                return menu;
            }
        }

        #endregion
    }



    public class DeleteConfirmEventArgs : EventArgs
    {
        public DeleteConfirmEventArgs(string caption, string message)
        {
            Caption = caption;
            Message = message;
        }

        public string Caption { get; }
        public string Message { get; }
        public bool? DialogResult { get; set; }
    }

}
