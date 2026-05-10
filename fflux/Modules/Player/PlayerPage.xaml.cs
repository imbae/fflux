using System.Windows.Controls;
using System.Windows.Input;

namespace fflux.UI.Modules.Player;

public partial class PlayerPage : Page
{
    public PlayerViewModel ViewModel { get; }

    public PlayerPage(PlayerViewModel viewModel)
    {
        ViewModel   = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // 페이지 로드 시 RootGrid에 키보드 포커스를 설정합니다.
        // 이렇게 해야 KeyDown 이벤트가 다른 요소에 포커스가 없을 때도 동작합니다.
        Loaded += (_, _) => RootGrid.Focus();
    }

    // ── DragDrop 핸들러 ───────────────────────────────────────────────

    private void OnVideoDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnVideoDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            _ = ViewModel.OpenDroppedFileAsync(files[0]);
    }

    // ── 키보드 단축키 ────────────────────────────────────────────────
    //
    // Slider가 포커스를 가지면 ← → 키를 먼저 소비합니다.
    // RootGrid.KeyDown은 Slider가 처리하지 않은 경우에만 도달하므로,
    // Slider의 IsTabStop="False"는 설정하지 않고 대신 PreviewKeyDown 단계에서
    // Slider 이외의 요소가 포커스를 가질 때 처리합니다.

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is Slider) return;  // Slider가 이미 처리

        switch (e.Key)
        {
            case Key.Space:
                if (ViewModel.IsPlaying)
                    ViewModel.PauseCommand.Execute(null);
                else if (ViewModel.IsFileOpen)
                    ViewModel.PlayCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Left:
                _ = ViewModel.SeekRelativeAsync(-5.0);
                e.Handled = true;
                break;

            case Key.Right:
                _ = ViewModel.SeekRelativeAsync(+5.0);
                e.Handled = true;
                break;

            case Key.M:
                ViewModel.ToggleMuteCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
