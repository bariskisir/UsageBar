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
pub const NIIF_INFO: u32 = 0x00000001;

pub const MF_STRING: u32 = 0x00000000;
pub const TPM_RIGHTBUTTON: u32 = 0x0002;
pub const TPM_RETURNCMD: u32 = 0x0100;

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
}

#[link(name = "kernel32")]
extern "system" {
    pub fn GetModuleHandleW(lpModuleName: *const u16) -> Hinstance;
}

#[link(name = "shell32")]
extern "system" {
    pub fn Shell_NotifyIconW(dwMessage: u32, lpData: *const NotifyIconDataW) -> i32;
}
