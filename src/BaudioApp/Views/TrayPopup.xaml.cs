using System.Windows.Controls;
using BaudioApp.ViewModels;

namespace BaudioApp.Views;

public partial class TrayPopup : UserControl
{
    public TrayPopup()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (DataContext is TrayPopupViewModel vm)
                vm.SetPopupVisible((bool)e.NewValue);
        };
    }
}
