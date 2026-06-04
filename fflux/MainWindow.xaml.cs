using System;
using System.Windows;
using fflux.UI.Modules.Player;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace fflux.UI;

public partial class MainWindow : FluentWindow
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INavigationService _navigationService;
    private readonly IContentDialogService _contentDialogService;
    private readonly ISnackbarService _snackbarService;

    public MainWindowViewModel ViewModel { get; }

    public MainWindow(
        MainWindowViewModel viewModel,
        IServiceProvider serviceProvider,
        INavigationService navigationService,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        _serviceProvider = serviceProvider;
        _navigationService = navigationService;
        _contentDialogService = contentDialogService;
        _snackbarService = snackbarService;

        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // ContentDialogHost 연결
        _contentDialogService.SetDialogHost(RootContentDialogHost);

        // SnackbarPresenter 연결
        _snackbarService.SetSnackbarPresenter(RootSnackbarPresenter);

        // NavigationView에 DI 컨테이너 연결 (페이지 인스턴스 해석용)
        RootNavigation.SetServiceProvider(_serviceProvider);

        // INavigationService에 NavigationView 연결 (ViewModel 탐색용)
        _navigationService.SetNavigationControl(RootNavigation);

        // 시작 페이지: Player
        RootNavigation.Navigate(typeof(PlayerPage));
    }
}
