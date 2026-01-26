using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CPRLib;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SkiaSharp;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using WinUIEx;
using Windows.ApplicationModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CPRTouchVision.Windows
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class InitWindow : WindowEx
    {
        InitWindowModel Model = new();

        public InitWindow()
        {
            this.InitializeComponent();
            Activated += OnActivated;
            ExtendsContentIntoTitleBar = true;
            IsMinimizable = false;
            IsMaximizable = false;
            IsTitleBarVisible = false;
        }

        private void OnActivated(object? sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == WindowActivationState.CodeActivated || e.WindowActivationState == WindowActivationState.PointerActivated)
            {
                Init();
            }
        }

        public async void Init()
        {
            if (await Model.Init())
            {
                App.Current.Setup();
                Close();
            }
        }

        private void OnErrorActionClicked(object? sender, RoutedEventArgs e)
        {
            if (Model.ShouldExit)
            {
                Close();
            }
            else
            {
                Refresh();
            }
        }

        private async void Refresh()
        {
            if (await Model.Refresh())
            {
                App.Current.Setup();
                Close();
            }
        }
    }

    partial class InitWindowModel : ObservableObject
    {
        [ObservableProperty]
        bool _isLoading = true;

        [ObservableProperty]
        Visibility _loaderVisibility = Visibility.Visible;

        [ObservableProperty]
        string? _errorMessage = null;

        [ObservableProperty]
        string? _errorActionTitle = null;

        [ObservableProperty]
        Visibility _errorVisibility = Visibility.Collapsed;

        [ObservableProperty]
        bool _shouldExit = false;

        public string Copyright
        {
            get
            {
                var version = Package.Current.Id.Version;
                return $"Designed by CPR Software exclusively for KIDSjumpTECH™\nCopyright © {DateTime.Now.Year}";
            }
        }

        public async Task<bool> Init()
        {
            var bitmap = await GetBitmap(Utils.Images[7]);
            Api.Instance.Setup(bitmap);
            LicenseManager.Instance.LoadLicense();
            await LicenseManager.Instance.RefreshLicense();
            var result = CheckLicense();
            if (result)
            {
 //               LidarCoordinator.Instance.Setup();
            }
            IsLoading = false;
            return result;
        }

        private async Task<SKBitmap> GetBitmap(string path)
        {
            var uri = new Uri(path);
            var file = await StorageFile.GetFileFromApplicationUriAsync(uri);

            using (var openedStream = await file.OpenAsync(FileAccessMode.Read))
            {
                using (var stream = openedStream.AsStreamForRead())
                {
                    using (var codec = SKCodec.Create(stream))
                    {
                        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, codec.Info.ColorType, SKAlphaType.Unpremul, codec.Info.ColorSpace);
                        var bitmap = new SKBitmap(info);
                        codec.GetPixels(bitmap.Info, bitmap.GetPixels());
                        return bitmap;
                    }
                }
            }
        }

        public async Task<bool> Refresh()
        {
            IsLoading = true;
            var result = true;
            ErrorMessage = null;
            ErrorActionTitle = null;
            ShouldExit = false;
            ErrorVisibility = Visibility.Collapsed;

            var (success, error) = await LicenseManager.Instance.RefreshLicense();
            await Task.Delay(500);

            if (success)
            {
                result = CheckLicense();

               // if (result)
                    //LidarCoordinator.Instance.Setup();
            }
            else
            {
                ErrorMessage = error;
                ErrorActionTitle = "Refresh";
                ErrorVisibility = Visibility.Visible;
                result = false;
            }

            IsLoading = false;
            return result;
        }

        private bool CheckLicense()
        {
            if (LicenseManager.Instance.NeedsRefresh)
            {
                ErrorMessage = "Your KIDSjumpTECH™ license needs to be refreshed.";
                ErrorActionTitle = "Refresh";
                ErrorVisibility = Visibility.Visible;
                return false;
            }
            else if (!LicenseManager.Instance.IsActive)
            {
                ErrorMessage = "Your KIDSjumpTECH™ license has expired.";
                ErrorActionTitle = "Exit";
                ShouldExit = true;
                ErrorVisibility = Visibility.Visible;
                return false;
            }

            return true;
        }

        partial void OnIsLoadingChanged(bool oldValue, bool newValue)
        {
            LoaderVisibility = IsLoading ? Visibility.Visible : Visibility.Collapsed;
            if (IsLoading)
            {
                ErrorVisibility = Visibility.Collapsed;
            }
        }
    }
}
