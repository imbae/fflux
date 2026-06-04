using System.Windows.Controls;
using System.Windows.Input;

namespace fflux.UI.Modules.SubtitleEditor;

/// <summary>
/// 자막 편집기 페이지 코드비하인드.
/// <para>순수 View 동작만 담당합니다:</para>
/// <list type="bullet">
///   <item>비디오 / SRT / VTT 파일 드래그·드롭 → <see cref="SubtitleEditorViewModel.DropFileAsync"/></item>
///   <item><see cref="SubtitleEditorViewModel.RequestScrollToCue"/> → DataGrid.ScrollIntoView</item>
///   <item>키보드 단축키 (Ctrl+O, Ctrl+S, Ins, Ctrl+Del, Space)</item>
/// </list>
/// </summary>
public partial class SubtitleEditorPage : Page
{
    private SubtitleEditorViewModel? _vm;

    public SubtitleEditorPage(SubtitleEditorViewModel viewModel)
    {
        InitializeComponent();

        _vm = viewModel;
        DataContext = _vm;

        _vm.RequestScrollToCue += OnRequestScrollToCue;

        Unloaded += (_, _) =>
        {
            if (_vm is not null)
                _vm.RequestScrollToCue -= OnRequestScrollToCue;
        };
    }

    // ── 드래그·드롭 ────────────────────────────────────────────────

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        // 파일 종류 판별 및 라우팅은 ViewModel이 담당합니다.
        _ = _vm?.DropFileAsync(files[0]);

        e.Handled = true;
    }

    // ── 키보드 단축키 ────────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null) return;

        // DataGrid 편집 중이면 단축키를 처리하지 않음
        if (CueDataGrid.IsKeyboardFocusWithin && CueDataGrid.CurrentCell.IsValid)
        {
            var currentColumn = CueDataGrid.CurrentCell.Column;
            if (currentColumn is DataGridTextColumn)
                return; // 셀 편집 중
        }

        switch (e.Key)
        {
            case Key.O when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                if (_vm.OpenFileCommand.CanExecute(null))
                    _vm.OpenFileCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.S when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                if (_vm.SaveFileCommand.CanExecute(null))
                    _vm.SaveFileCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Insert:
                if (_vm.AddCueBelowCommand.CanExecute(null))
                    _vm.AddCueBelowCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Delete when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                if (_vm.DeleteCueCommand.CanExecute(null))
                    _vm.DeleteCueCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Space:
                // Space: 재생/일시정지 토글 — ViewModel에 위임
                _vm.TogglePlayPause();
                e.Handled = true;
                break;
        }
    }

    // ── 재생 위치 → DataGrid 자동 스크롤 ─────────────────────────────

    private void OnRequestScrollToCue(object? sender, SubtitleCueViewModel cue)
    {
        CueDataGrid.ScrollIntoView(cue);
    }
}
