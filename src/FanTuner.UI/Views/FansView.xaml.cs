using System.Windows.Controls;
using FanTuner.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FanTuner.UI.Views;

public partial class FansView : UserControl
{
    public FansView()
    {
        InitializeComponent();

        DataContext = App.Services.GetRequiredService<FansViewModel>();

        Loaded += (s, e) =>
        {
            if (DataContext is FansViewModel vm)
            {
                vm.Initialize();
            }
        };
    }
}
