using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WaNut.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public ObservableCollection<string> Servers { get; } = [];

    [RelayCommand]
    private void CreateServer()
    {
        // The Create Server wizard arrives in step 2.
    }
}
