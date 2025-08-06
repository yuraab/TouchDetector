using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIEx;

namespace CPRTouchVision
{
    public sealed partial class TestWindow : WindowEx
    {
        public TestWindow()
        {
            App.Log("Initializing Test Window");
            InitializeComponent();
            App.Log("Initializing Test Window - Done");
        }
    }
}