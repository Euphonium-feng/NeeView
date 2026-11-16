using NeeView.Windows.Media;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NeeView.Windows;

namespace NeeView
{
    public partial class ImageEffectView : UserControl
    {
        private const string ShaderDragFormat = "NeeView.ShaderChainItem";
        private readonly ImageEffectViewModel _vm;
        private bool _isFocusRequest;
        private Point _shaderDragStart;
        private ShaderChainItemViewModel? _shaderDragItem;


        public ImageEffectView()
        {
            InitializeComponent();

            _vm = new ImageEffectViewModel();
            _vm.RenameProfileRequested += ViewModel_RenameRequest;
            _vm.DeleteConfirmRequested += ViewModel_DeleteConfirmRequested;

            this.DataContext = _vm;

            this.IsVisibleChanged += ImageEffectView_IsVisibleChanged;

            Debug.WriteLine($"> Create: {nameof(ImageEffectView)}");
        }


        // 単キーのショートカット無効
        private void Control_KeyDown_IgnoreSingleKeyGesture(object sender, KeyEventArgs e)
        {
            KeyExGesture.AddFilter(KeyExGestureFilter.All);
        }

        private void ImageEffectView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_isFocusRequest && this.IsVisible)
            {
                this.Focus();
                _isFocusRequest = false;
            }
        }

        // フィルターパラメータリセット
        private void Reset(object sender, RoutedEventArgs e)
        {
            _vm.ResetValue();
            ShaderParameterPopup.IsOpen = false;
        }

        private void ShaderParameterButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement button || button.DataContext is not ShaderChainItemViewModel item)
            {
                return;
            }

            if (ShaderParameterPopup.IsOpen && ReferenceEquals(ShaderParameterPopup.PlacementTarget, button))
            {
                ShaderParameterPopup.IsOpen = false;
                return;
            }

            _vm.SelectedShaderChainItem = item;
            ShaderParameterPopup.PlacementTarget = button;
            ShaderParameterPopup.HorizontalOffset = -8;
            ShaderParameterPopup.IsOpen = true;
        }

        private void ShaderParameterReset_Click(object sender, RoutedEventArgs e)
        {
            _vm.ResetShaderParametersCommand.Execute(_vm.SelectedShaderChainItem);
        }

        private void AddShaderButton_Click(object sender, RoutedEventArgs e)
        {
            if (ShaderSelector.SelectedValue is string path)
            {
                _vm.AddShaderCommand.Execute(path);
            }
        }

        private void ShaderChainList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _shaderDragStart = e.GetPosition(ShaderChainList);
            _shaderDragItem = FindShaderItem(e.OriginalSource as DependencyObject);
        }

        private void ShaderChainList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _shaderDragItem is null)
            {
                return;
            }
            var position = e.GetPosition(ShaderChainList);
            if (Math.Abs(position.X - _shaderDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _shaderDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }
            DragDrop.DoDragDrop(ShaderChainList, new DataObject(ShaderDragFormat, _shaderDragItem), DragDropEffects.Move);
            _shaderDragItem = null;
        }

        private void ShaderChainList_PreviewDragOver(object sender, DragEventArgs e)
        {
            ListBoxDragSortExtension.PreviewDragOver(sender, e, ShaderDragFormat);
        }

        private void ShaderChainList_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(ShaderDragFormat) is not ShaderChainItemViewModel item)
            {
                return;
            }
            var target = FindShaderItem(e.OriginalSource as DependencyObject);
            if (target is null || ReferenceEquals(item, target))
            {
                return;
            }
            var items = _vm.ShaderChain;
            var oldIndex = items.IndexOf(item);
            var newIndex = items.IndexOf(target);
            if (oldIndex < 0 || newIndex < 0) return;
            Config.Current.ImageResizeFilter.Shaders.Items.Move(oldIndex, newIndex);
            _vm.ReloadShaderChain();
            e.Handled = true;
        }

        private ShaderChainItemViewModel? FindShaderItem(DependencyObject? source)
        {
            while (source is not null)
            {
                if (source is FrameworkElement element && element.DataContext is ShaderChainItemViewModel item)
                {
                    return item;
                }
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        public void FocusAtOnce()
        {
            var focused = this.Focus();
            if (!focused)
            {
                _isFocusRequest = true;
            }
        }

        private async void ViewModel_RenameRequest(object? sender, EventArgs e)
        {
            var comboBox = this.EffectProfileComboBox;
            comboBox.UpdateLayout();

            var textBlock = VisualTreeUtility.FindVisualChild<TextBlock>(comboBox, "FileNameTextBlock");
            if (textBlock is null) return;

            var rename = new EffectProfileRenameControl(new RenameControlSource(comboBox, textBlock), Rename);
            await rename.ShowAsync();

            bool Rename(string name)
            {
                return _vm.RenameProfile(name);
            }
        }

        private void ViewModel_DeleteConfirmRequested(object? sender, DeleteConfirmEventArgs e)
        {
            var dialog = new MessageDialog(e.Caption, e.Message);
            dialog.Owner = Window.GetWindow(this);
            dialog.Commands.Add(UICommands.Delete);
            dialog.Commands.Add(UICommands.Cancel);
            var result = dialog.ShowDialog();
            e.DialogResult = result.IsPossible;
        }
    }


    public class EffectProfileRenameControl : RenameControl
    {
        private readonly Func<string, bool> _renameFunc;

        public EffectProfileRenameControl(RenameControlSource source, Func<string, bool> renameFunc) : base(source)
        {
            _renameFunc = renameFunc;
            this.IsInvalidSeparatorChars = true;
            this.IsInvalidFileNameChars = true;
        }

        protected override async Task<bool> OnRenameAsync(string oldValue, string newValue)
        {
            if (oldValue == newValue) return true;

            var result = _renameFunc(newValue);
            return result;
        }
    }

}
