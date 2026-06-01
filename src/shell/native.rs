//! Raw Win32 FFI declarations — mirrors the C# `NativeMethods.cs` P/Invoke.
//! Uses `extern "system"` directly to avoid version-dependent type wrapping.

#![allow(non_snake_case, dead_code)]

use std::ffi::c_void;

// ---------------------------------------------------------------------------
// Handle wrappers that are Send + Sync (Win32 handles are thread-safe).
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(transparent)]
pub struct Hwnd(pub isize);
unsafe impl Send for Hwnd {}
unsafe impl Sync for Hwnd {}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(transparent)]
pub struct Hicon(pub isize);
unsafe impl Send for Hicon {}
unsafe impl Sync for Hicon {}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(transparent)]
pub struct Hmenu(pub isize);
unsafe impl Send for Hmenu {}
unsafe impl Sync for Hmenu {}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(transparent)]
pub struct Hinstance(pub isize);
unsafe impl Send for Hinstance {}
unsafe impl Sync for Hinstance {}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

pub const WM_APP: u32 = 0x8000;
pub const WM_DESTROY: u32 = 0x0002;
pub const WM_PAINT: u32 = 0x000F;
pub const WM_ERASEBKGND: u32 = 0x0014;
pub const WM_CONTEXTMENU: u32 = 0x007B;
pub const WM_RBUTTONUP: u32 = 0x0205;
pub const WM_NULL: u32 = 0x0000;

pub const NIF_MESSAGE: u32 = 0x00000001;
pub const NIF_ICON: u32 = 0x00000002;
pub const NIF_TIP: u32 = 0x00000004;
pub const NIF_INFO: u32 = 0x00000010;
pub const NIF_SHOWTIP: u32 = 0x00000080;
pub const NIM_ADD: u32 = 0x00000000;
pub const NIM_MODIFY: u32 = 0x00000001;
pub const NIM_DELETE: u32 = 0x00000002;
pub const NIM_SETVERSION: u32 = 0x00000004;
pub const NOTIFYICON_VERSION_4: u32 = 4;
pub const NIIF_INFO: u32 = 0x00000001;

// Tray notification events delivered via the callback message when the icon
// uses NOTIFYICON_VERSION_4. The event id is in the low word of `lParam`.
// These are WM_USER-based (0x0400), NOT WM_APP-based (0x8000).
pub const WM_USER: u32 = 0x0400;
pub const NIN_POPUPOPEN: u32 = WM_USER + 6; // 0x0406
pub const NIN_POPUPCLOSE: u32 = WM_USER + 7; // 0x0407

pub const MF_STRING: u32 = 0x00000000;
pub const MF_CHECKED: u32 = 0x00000008;
pub const MF_UNCHECKED: u32 = 0x00000000;
pub const MF_POPUP: u32 = 0x00000010;
pub const MF_BYCOMMAND: u32 = 0x00000000;
pub const MFS_CHECKED: u32 = 0x00000008;
pub const TPM_RIGHTBUTTON: u32 = 0x0002;
pub const TPM_RETURNCMD: u32 = 0x0100;

// Window styles for the custom tooltip popup.
pub const WS_POPUP: u32 = 0x80000000;
pub const WS_EX_TOOLWINDOW: u32 = 0x00000080;
pub const WS_EX_TOPMOST: u32 = 0x00000008;
pub const WS_EX_LAYERED: u32 = 0x00080000;
pub const WS_EX_NOACTIVATE: u32 = 0x08000000;

pub const SW_HIDE: i32 = 0;
pub const SW_SHOWNOACTIVATE: i32 = 4;

pub const SWP_NOSIZE: u32 = 0x0001;
pub const SWP_NOMOVE: u32 = 0x0002;
pub const SWP_NOZORDER: u32 = 0x0004;
pub const SWP_NOACTIVATE: u32 = 0x0010;
pub const HWND_TOPMOST: isize = -1;

/// `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2` — crisp WebView2 rendering.
pub const DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2: isize = -4;

pub const LWA_ALPHA: u32 = 0x00000002;
pub const SPI_GETWORKAREA: u32 = 0x0030;

/// COM apartment-threaded init, required before creating a WebView2 control.
pub const COINIT_APARTMENTTHREADED: u32 = 0x2;

// GDI text/background drawing.
pub const TRANSPARENT: i32 = 1;
pub const PS_SOLID: i32 = 0;
pub const DEFAULT_CHARSET: u32 = 1;
pub const CLEARTYPE_QUALITY: u32 = 5;
pub const FW_NORMAL: i32 = 400;
pub const FW_MEDIUM: i32 = 500;
pub const FW_SEMIBOLD: i32 = 600;
pub const FW_BOLD: i32 = 700;

// ---------------------------------------------------------------------------
// Structs
// ---------------------------------------------------------------------------

#[repr(C)]
pub struct WndClassExW {
    pub cbSize: u32,
    pub style: u32,
    pub lpfnWndProc: Option<unsafe extern "system" fn(Hwnd, u32, usize, isize) -> isize>,
    pub cbClsExtra: i32,
    pub cbWndExtra: i32,
    pub hInstance: Hinstance,
    pub hIcon: Hicon,
    pub hCursor: isize,
    pub hbrBackground: isize,
    pub lpszMenuName: *const u16,
    pub lpszClassName: *const u16,
    pub hIconSm: Hicon,
}

#[repr(C)]
pub struct NotifyIconDataW {
    pub cbSize: u32,
    pub hWnd: Hwnd,
    pub uID: u32,
    pub uFlags: u32,
    pub uCallbackMessage: u32,
    pub hIcon: Hicon,
    pub szTip: [u16; 128],
    pub dwState: u32,
    pub dwStateMask: u32,
    pub szInfo: [u16; 256],
    pub uTimeoutOrVersion: u32,
    pub szInfoTitle: [u16; 64],
    pub dwInfoFlags: u32,
    pub guidItem: [u8; 16],
    pub hBalloonIcon: Hicon,
}

#[repr(C)]
pub struct Point {
    pub x: i32,
    pub y: i32,
}

/// Identifies a tray icon for `Shell_NotifyIconGetRect`. `repr(C)` inserts the
/// padding required to 8-byte-align `hWnd`, so `size_of` matches the Win32
/// `NOTIFYICONIDENTIFIER` (40 bytes on x64).
#[repr(C)]
pub struct NotifyIconIdentifier {
    pub cbSize: u32,
    pub hWnd: Hwnd,
    pub uID: u32,
    pub guidItem: [u8; 16],
}

#[repr(C)]
#[derive(Debug, Clone, Copy, Default)]
pub struct Rect {
    pub left: i32,
    pub top: i32,
    pub right: i32,
    pub bottom: i32,
}

#[repr(C)]
#[derive(Debug, Clone, Copy, Default)]
pub struct Size {
    pub cx: i32,
    pub cy: i32,
}

#[repr(C)]
pub struct PaintStruct {
    pub hdc: isize,
    pub f_erase: i32,
    pub rc_paint: Rect,
    pub f_restore: i32,
    pub f_inc_update: i32,
    pub rgb_reserved: [u8; 32],
}

#[repr(C)]
pub struct Msg {
    pub hwnd: Hwnd,
    pub message: u32,
    pub wParam: usize,
    pub lParam: isize,
    pub time: u32,
    pub pt: Point,
}

// ---------------------------------------------------------------------------
// FFI functions
// ---------------------------------------------------------------------------

#[link(name = "user32")]
extern "system" {
    pub fn RegisterClassExW(lpwcx: *const WndClassExW) -> u16;
    pub fn CreateWindowExW(
        dwExStyle: u32,
        lpClassName: *const u16,
        lpWindowName: *const u16,
        dwStyle: u32,
        x: i32,
        y: i32,
        nWidth: i32,
        nHeight: i32,
        hWndParent: Hwnd,
        hMenu: Hmenu,
        hInstance: Hinstance,
        lpParam: *const c_void,
    ) -> Hwnd;
    pub fn DestroyWindow(hWnd: Hwnd) -> i32;
    pub fn DefWindowProcW(hWnd: Hwnd, Msg: u32, wParam: usize, lParam: isize) -> isize;
    pub fn GetMessageW(lpMsg: *mut Msg, hWnd: Hwnd, wMsgFilterMin: u32, wMsgFilterMax: u32) -> i32;
    pub fn TranslateMessage(lpMsg: *const Msg) -> i32;
    pub fn DispatchMessageW(lpMsg: *const Msg) -> isize;
    pub fn PostQuitMessage(nExitCode: i32);
    pub fn PostMessageW(hWnd: Hwnd, Msg: u32, wParam: usize, lParam: isize) -> i32;
    pub fn GetCursorPos(lpPoint: *mut Point) -> i32;
    pub fn SetForegroundWindow(hWnd: Hwnd) -> i32;
    pub fn CreatePopupMenu() -> Hmenu;
    pub fn AppendMenuW(hMenu: Hmenu, uFlags: u32, uIDNewItem: usize, lpNewItem: *const u16) -> i32;
    pub fn TrackPopupMenuEx(
        hMenu: Hmenu,
        uFlags: u32,
        x: i32,
        y: i32,
        hWnd: Hwnd,
        lptpm: *const c_void,
    ) -> i32;
    pub fn DestroyMenu(hMenu: Hmenu) -> i32;
    pub fn CheckMenuItem(hMenu: Hmenu, uIDCheckItem: u32, uCheck: u32) -> i32;
    pub fn CreateIcon(
        hInstance: Hinstance,
        nWidth: i32,
        nHeight: i32,
        cPlanes: u8,
        cBitsPixel: u8,
        lpbANDbits: *const u8,
        lpbXORbits: *const u8,
    ) -> Hicon;
    pub fn DestroyIcon(hIcon: Hicon) -> i32;
    pub fn ShowWindow(hWnd: Hwnd, nCmdShow: i32) -> i32;
    pub fn SetWindowPos(
        hWnd: Hwnd,
        hWndInsertAfter: isize,
        x: i32,
        y: i32,
        cx: i32,
        cy: i32,
        uFlags: u32,
    ) -> i32;
    pub fn GetClientRect(hWnd: Hwnd, lpRect: *mut Rect) -> i32;
    pub fn InvalidateRect(hWnd: Hwnd, lpRect: *const Rect, bErase: i32) -> i32;
    pub fn BeginPaint(hWnd: Hwnd, lpPaint: *mut PaintStruct) -> isize;
    pub fn EndPaint(hWnd: Hwnd, lpPaint: *const PaintStruct) -> i32;
    pub fn FillRect(hDC: isize, lprc: *const Rect, hbr: isize) -> i32;
    pub fn FrameRect(hDC: isize, lprc: *const Rect, hbr: isize) -> i32;
    pub fn GetDC(hWnd: Hwnd) -> isize;
    pub fn ReleaseDC(hWnd: Hwnd, hDC: isize) -> i32;
    pub fn SetWindowRgn(hWnd: Hwnd, hRgn: isize, bRedraw: i32) -> i32;
    pub fn SetLayeredWindowAttributes(hWnd: Hwnd, crKey: u32, bAlpha: u8, dwFlags: u32) -> i32;
    pub fn SystemParametersInfoW(
        uiAction: u32,
        uiParam: u32,
        pvParam: *mut c_void,
        fWinIni: u32,
    ) -> i32;
    pub fn SetProcessDpiAwarenessContext(value: isize) -> i32;
    pub fn GetDpiForWindow(hWnd: Hwnd) -> u32;
}

#[link(name = "gdi32")]
extern "system" {
    pub fn CreateSolidBrush(color: u32) -> isize;
    pub fn DeleteObject(hObject: isize) -> i32;
    pub fn SelectObject(hDC: isize, hObject: isize) -> isize;
    pub fn SetTextColor(hDC: isize, color: u32) -> u32;
    pub fn SetBkMode(hDC: isize, mode: i32) -> i32;
    pub fn TextOutW(hDC: isize, x: i32, y: i32, lpString: *const u16, c: i32) -> i32;
    pub fn GetTextExtentPoint32W(hDC: isize, lpString: *const u16, c: i32, psizl: *mut Size)
        -> i32;
    pub fn CreateRoundRectRgn(x1: i32, y1: i32, x2: i32, y2: i32, w: i32, h: i32) -> isize;
    pub fn CreatePen(iStyle: i32, cWidth: i32, color: u32) -> isize;
    pub fn RoundRect(hDC: isize, left: i32, top: i32, right: i32, bottom: i32, width: i32, height: i32)
        -> i32;
    #[allow(clippy::too_many_arguments)]
    pub fn CreateFontW(
        cHeight: i32,
        cWidth: i32,
        cEscapement: i32,
        cOrientation: i32,
        cWeight: i32,
        bItalic: u32,
        bUnderline: u32,
        bStrikeOut: u32,
        iCharSet: u32,
        iOutPrecision: u32,
        iClipPrecision: u32,
        iQuality: u32,
        iPitchAndFamily: u32,
        pszFaceName: *const u16,
    ) -> isize;
}

#[link(name = "kernel32")]
extern "system" {
    pub fn GetModuleHandleW(lpModuleName: *const u16) -> Hinstance;
}

#[link(name = "ole32")]
extern "system" {
    pub fn CoInitializeEx(pvReserved: *const c_void, dwCoInit: u32) -> i32;
}

#[link(name = "shell32")]
extern "system" {
    pub fn Shell_NotifyIconW(dwMessage: u32, lpData: *const NotifyIconDataW) -> i32;
    pub fn Shell_NotifyIconGetRect(
        identifier: *const NotifyIconIdentifier,
        iconLocation: *mut Rect,
    ) -> i32;
}
