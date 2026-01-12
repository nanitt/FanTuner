using System.Windows.Controls;
using System.Windows.Input;
using FanTuner.Core.Models;
using FanTuner.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FanTuner.UI.Views;

public partial class ProfilesView : UserControl
{
    public ProfilesView()
    {
        InitializeComponent();

        DataContext = App.Services.GetRequiredService<ProfilesViewModel>();

        Loaded += (s, e) =>
        {
            if (DataContext is ProfilesViewModel vm)
            {
                vm.Initialize();
            }
        };
    }

    private void OnCurveSelected(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is FanCurve curve)
        {
            if (DataContext is ProfilesViewModel vm)
            {
                vm.SelectedCurve = curve;
            }
        }
    }
}
