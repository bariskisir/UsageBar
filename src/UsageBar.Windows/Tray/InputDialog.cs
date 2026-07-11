using System.Text;

namespace UsageBar.Windows.Tray;

internal static class InputDialog
{
    private static string? _className;
    private static nint _hwnd;
    private static nint _editHwnd;
    private static string? _result;
    private static bool _completed;

    private const int IdOk = 1;
    private const int IdCancel = 2;
    private const int DlgW = 580;
    private const int DlgH = 235;
    private const int Margin = 12;
    private const int BtnW = 90;
    private const int BtnH = 30;

    private const int PromptY = 12;
    private const int PromptH = 66;
    private const int EditY = 86;
    private const int EditH = 26;
    private const int BtnY = 136;

    /// <summary>
    /// Shows a modal input dialog. Returns the entered text, an empty string when the user
    /// clears the field and clicks OK, or <see langword="null"/> when the user cancels.
    /// </summary>
    public static string? Show(nint parentHwnd, string title, string prompt, string? initialValue)
    {
        _completed = false;
        _result = null;
        _editHwnd = 0;

        var instance = NativeMethods.GetModuleHandle(null);

        if (_className is null)
        {
            _className = NativeMethods.RegisterWindowClass("InputDialog", WindowProc, instance);
            if (_className is null)
            {
                return null;
            }
        }

        var screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        var screenH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        var x = (screenW - DlgW) / 2;
        var y = (screenH - DlgH) / 3;

        _hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_DLGMODALFRAME,
            _className,
            title,
            NativeMethods.WS_CAPTION | NativeMethods.WS_SYSMENU,
            x, y,
            DlgW, DlgH,
            parentHwnd, 0, instance, 0);

        if (_hwnd == 0)
        {
            return null;
        }

        // Explicitly set title after creation (works around a marshalling edge case
        // where the CreateWindowEx lpWindowName parameter may not render fully).
        NativeMethods.SetWindowText(_hwnd, title);

        var editLeft = Margin;
        var editRight = DlgW - Margin;
        var editWidth = editRight - editLeft;

        // Prompt text
        NativeMethods.CreateWindowEx(0, "STATIC", prompt,
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE,
            editLeft, PromptY, editWidth, PromptH, _hwnd, 0, instance, 0);

        // Edit control
        _editHwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_CLIENTEDGE, "EDIT", initialValue ?? "",
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.WS_BORDER | NativeMethods.ES_AUTOHSCROLL,
            editLeft, EditY, editWidth, EditH, _hwnd, (nint)100, instance, 0);

        // OK button (default)
        NativeMethods.CreateWindowEx(0, "BUTTON", "OK",
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.BS_DEFPUSHBUTTON,
            DlgW - BtnW * 2 - Margin, BtnY, BtnW, BtnH, _hwnd, (nint)IdOk, instance, 0);

        // Cancel button
        NativeMethods.CreateWindowEx(0, "BUTTON", "Cancel",
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP,
            DlgW - BtnW - Margin, BtnY, BtnW, BtnH, _hwnd, (nint)IdCancel, instance, 0);

        NativeMethods.EnableWindow(parentHwnd, false);
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        NativeMethods.SetFocus(_editHwnd);

        while (!_completed && NativeMethods.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (!NativeMethods.IsDialogMessage(_hwnd, ref msg))
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
        }

        NativeMethods.EnableWindow(parentHwnd, true);
        NativeMethods.SetForegroundWindow(parentHwnd);
        NativeMethods.DestroyWindow(_hwnd);
        _hwnd = 0;

        return _result;
    }

    private static nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_COMMAND:
                var code = NativeMethods.HiWord(wParam);
                var id = NativeMethods.LoWord(wParam);
                if (code == NativeMethods.BN_CLICKED)
                {
                    if (id == IdOk)
                    {
                        var len = (int)NativeMethods.GetWindowTextLengthW(_editHwnd);
                        var sb = new StringBuilder(len + 1);
                        NativeMethods.GetWindowText(_editHwnd, sb, sb.Capacity);
                        _result = sb.ToString();
                        _completed = true;
                        return 0;
                    }

                    if (id == IdCancel)
                    {
                        _result = null;
                        _completed = true;
                        return 0;
                    }
                }
                break;

            case NativeMethods.WM_CLOSE:
                _result = null;
                _completed = true;
                return 0;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }
}