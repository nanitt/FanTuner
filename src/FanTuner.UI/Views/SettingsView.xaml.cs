using System.Windows.Controls;
using FanTuner.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FanTuner.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        DataContext = App.Services.GetRequiredService<SettingsViewModel>();

        Loaded += (s, e) =>
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.Initialize();
            }
        };
    }
}
