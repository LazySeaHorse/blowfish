using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageCaptionSearch.Core.Models;

namespace ImageCaptionSearch.UI.ViewModels;

public partial class ImageDetailViewModel : ViewModelBase
{
    private readonly SearchResultViewModel _result;
    private readonly Action _onBack;

    public SearchResultViewModel Result => _result;

    public ImageDetailViewModel(SearchResultViewModel result, Action onBack)
    {
        _result = result;
        _onBack = onBack;

        NavigateBackCommand = new RelayCommand(onBack);
        OpenImageCommand = new RelayCommand(OpenImage);
        ShowInExplorerCommand = new RelayCommand(ShowInExplorer);
        CopyCaptionCommand = new RelayCommand(CopyCaption);
    }

    public IRelayCommand NavigateBackCommand { get; }
    public IRelayCommand OpenImageCommand { get; }
    public IRelayCommand ShowInExplorerCommand { get; }
    public IRelayCommand CopyCaptionCommand { get; }

    private void OpenImage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_result.ImagePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private void ShowInExplorer()
    {
        try
        {
            var argument = "/select, \"" + _result.ImagePath + "\"";
            Process.Start("explorer.exe", argument);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private void CopyCaption()
    {
        // TODO: Access Avalonia clipboard
    }
}
