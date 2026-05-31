use std::sync::Mutex;
use tokio::sync::mpsc;

use crate::shell::native::{
    AppendMenuW, CreatePopupMenu, CreateWindowExW, DefWindowProcW, DestroyIcon, DestroyMenu,
    DestroyWindow, DispatchMessageW, GetCursorPos, GetMessageW, GetModuleHandleW, Hicon, Hmenu,
    Hwnd, Msg, NotifyIconDataW, Point, PostMessageW, PostQuitMessage, RegisterClassExW,
    SetForegroundWindow, Shell_NotifyIconW, TrackPopupMenuEx, TranslateMessage, WndClassExW,
    MF_STRING, NIF_ICON, NIF_MESSAGE, NIF_SHOWTIP, NIF_TIP, NIM_ADD, NIM_DELETE, NIM_MODIFY,
    TPM_RETURNCMD, TPM_RIGHTBUTTON, WM_APP, WM_CONTEXTMENU, WM_DESTROY, WM_NULL, WM_RBUTTONUP,
};

const ICON_ID: u32 = 1;
const REFRESH_COMMAND_ID: usize = 1001;
const EXIT_COMMAND_ID: usize = 1002;
const CALLBACK_MESSAGE: u32 = WM_APP + 1;

// ---------------------------------------------------------------------------
// Static channel — the Win32 window proc pushes events through this sender.
// ---------------------------------------------------------------------------

static EVENT_TX: std::sync::OnceLock<Mutex<Option<mpsc::UnboundedSender<TrayEvent>>>> =
    std::sync::OnceLock::new();

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

#[derive(Debug, Clone)]
pub enum TrayEvent {
    Refresh,
    Exit,
}

/// A system-tray icon backed by a hidden Win32 window.
///
/// All public methods use `&self` (interior mutability) so the instance can be
/// shared between threads.  `Hwnd` and `Hicon` are explicitly `Send + Sync`.
pub struct TrayIcon {
    window_handle: Hwnd,
    icon_handle: Mutex<Hicon>,
}

// SAFETY: Win32 handles (HWND, HICON) are safe to send and share between
// threads.  The Mutex provides safe interior mutability for the icon handle.
unsafe impl Send for TrayIcon {}
unsafe impl Sync for TrayIcon {}

impl TrayIcon {
    /// Creates the hidden window, registers the tray icon, and returns both the
    /// `TrayIcon` and the receiver for tray events.
    pub fn new() -> anyhow::Result<(Self, mpsc::UnboundedReceiver<TrayEvent>)> {
        let (tx, rx) = mpsc::unbounded_channel();
        EVENT_TX
            .set(Mutex::new(Some(tx)))
            .map_err(|_| anyhow::anyhow!("TrayIcon already initialised."))?;

        let instance = unsafe { GetModuleHandleW(std::ptr::null()) };
        if instance.0 == 0 {
            anyhow::bail!("Failed to get module handle.");
        }

        let class_name: Vec<u16> = format!(
            "UsageBarRustTrayWindow-{}\0",
            uuid::Uuid::new_v4().to_string().replace('-', "")
        )
        .encode_utf16()
        .collect();

        let wc = WndClassExW {
            cbSize: std::mem::size_of::<WndClassExW>() as u32,
            style: 0,
            lpfnWndProc: Some(window_proc),
            cbClsExtra: 0,
            cbWndExtra: 0,
            hInstance: instance,
            hIcon: Hicon(0),
            hCursor: 0,
            hbrBackground: 0,
            lpszMenuName: std::ptr::null(),
            lpszClassName: class_name.as_ptr(),
            hIconSm: Hicon(0),
        };

        let atom = unsafe { RegisterClassExW(&wc) };
        if atom == 0 {
            anyhow::bail!("Failed to register tray window class. Error: {}", std::io::Error::last_os_error());
        }

        let window_name: Vec<u16> = "UsageBarRust\0".encode_utf16().collect();

        let window_handle = unsafe {
            CreateWindowExW(
                0,
                class_name.as_ptr(),
                window_name.as_ptr(),
                0,
                0,
                0,
                0,
                0,
                Hwnd(-3), // HWND_MESSAGE
                Hmenu(0),
                instance,
                std::ptr::null(),
            )
        };

        if window_handle.0 == 0 {
            anyhow::bail!("Failed to create tray window. Error: {}", std::io::Error::last_os_error());
        }

        let icon_handle = Mutex::new(crate::shell::icon::create_usage_icon(None, None)?);
        add_icon(window_handle, *icon_handle.lock().unwrap(), "UsageBarRust\nLoading...")?;

        Ok((Self { window_handle, icon_handle }, rx))
    }

    /// Blocks the calling thread in the Win32 message pump.
    /// Must be called on the same thread that created the window.
    pub fn run_message_loop(&self) {
        let mut msg: Msg = unsafe { std::mem::zeroed() };
        loop {
            let ret = unsafe { GetMessageW(&mut msg, Hwnd(0), 0, 0) };
            if ret == 0 || ret == -1 {
                break;
            }
            unsafe {
                TranslateMessage(&msg);
                DispatchMessageW(&msg);
            }
        }
    }

    /// Updates the tooltip text (thread-safe, truncates to 127 chars).
    pub fn update_tooltip(&self, tooltip: &str) {
        let limited = limit_tooltip(tooltip);
        let mut data = notify_icon_data(self.window_handle);
        data.uFlags = NIF_TIP | NIF_SHOWTIP;

        let mut tip_buf = [0u16; 128];
        let tip_wide: Vec<u16> = limited.encode_utf16().take(127).collect();
        let len = tip_wide.len().min(127);
        tip_buf[..len].copy_from_slice(&tip_wide[..len]);
        data.szTip = tip_buf;

        unsafe {
            Shell_NotifyIconW(NIM_MODIFY, &data);
        }
    }

    /// Replaces the tray icon (thread-safe).
    pub fn update_icon(
        &self,
        codex_primary_used_percent: Option<f64>,
        codex_secondary_used_percent: Option<f64>,
    ) -> anyhow::Result<()> {
        let next_icon = crate::shell::icon::create_usage_icon(
            codex_primary_used_percent,
            codex_secondary_used_percent,
        )?;
        let mut guard = self.icon_handle.lock().unwrap();
        let previous = *guard;
        *guard = next_icon;

        let mut data = notify_icon_data(self.window_handle);
        data.uFlags = NIF_ICON;
        data.hIcon = next_icon;

        unsafe {
            Shell_NotifyIconW(NIM_MODIFY, &data);
        }

        if previous.0 != 0 {
            unsafe {
                DestroyIcon(previous);
            }
        }

        Ok(())
    }

    /// Returns the HWND for posting messages from other threads.
    #[allow(dead_code)]
    pub fn window_handle(&self) -> Hwnd {
        self.window_handle
    }
}

impl Drop for TrayIcon {
    fn drop(&mut self) {
        let data = notify_icon_data(self.window_handle);
        unsafe {
            Shell_NotifyIconW(NIM_DELETE, &data);
        }

        let icon = *self.icon_handle.lock().unwrap();
        if icon.0 != 0 {
            unsafe {
                DestroyIcon(icon);
            }
        }

        if self.window_handle.0 != 0 {
            unsafe {
                DestroyWindow(self.window_handle);
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Window procedure
// ---------------------------------------------------------------------------

extern "system" fn window_proc(hwnd: Hwnd, msg: u32, wparam: usize, lparam: isize) -> isize {
    if msg == CALLBACK_MESSAGE {
        let mouse_msg = (lparam as u64 & 0xffff) as u32;
        if mouse_msg == WM_RBUTTONUP || mouse_msg == WM_CONTEXTMENU {
            let _ = show_context_menu(hwnd);
        }
        return 0;
    }

    if msg == WM_DESTROY {
        unsafe { PostQuitMessage(0) };
        return 0;
    }

    unsafe { DefWindowProcW(hwnd, msg, wparam, lparam) }
}

fn show_context_menu(hwnd: Hwnd) -> anyhow::Result<()> {
    let mut point = Point { x: 0, y: 0 };
    let got_pos = unsafe { GetCursorPos(&mut point) };
    if got_pos == 0 {
        return Ok(());
    }

    let menu = unsafe { CreatePopupMenu() };
    if menu.0 == 0 {
        return Ok(());
    }

    let result = (|| -> anyhow::Result<i32> {
        let refresh_label: Vec<u16> = "Refresh\0".encode_utf16().collect();
        let exit_label: Vec<u16> = "Exit\0".encode_utf16().collect();

        unsafe {
            AppendMenuW(menu, MF_STRING, REFRESH_COMMAND_ID, refresh_label.as_ptr());
            AppendMenuW(menu, MF_STRING, EXIT_COMMAND_ID, exit_label.as_ptr());
            SetForegroundWindow(hwnd);

            let cmd = TrackPopupMenuEx(
                menu,
                TPM_RIGHTBUTTON | TPM_RETURNCMD,
                point.x,
                point.y,
                hwnd,
                std::ptr::null(),
            );

            // Post a benign message so the menu dismissal is processed.
            PostMessageW(hwnd, WM_NULL, 0, 0);
            Ok(cmd)
        }
    })();

    unsafe {
        DestroyMenu(menu);
    }

    if let Ok(cmd) = result {
        if cmd != 0 {
            let lock = EVENT_TX.get().and_then(|m| m.lock().ok());
            if let Some(guard) = lock {
                if let Some(ref tx) = *guard {
                    match cmd as usize {
                        REFRESH_COMMAND_ID => {
                            let _ = tx.send(TrayEvent::Refresh);
                        }
                        EXIT_COMMAND_ID => {
                            let _ = tx.send(TrayEvent::Exit);
                            // PostQuitMessage MUST be called from the thread that
                            // owns the message loop (this thread, inside window_proc).
                            unsafe { PostQuitMessage(0) };
                        }
                        _ => {}
                    }
                }
            }
        }
    }

    Ok(())
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

fn notify_icon_data(hwnd: Hwnd) -> NotifyIconDataW {
    NotifyIconDataW {
        cbSize: std::mem::size_of::<NotifyIconDataW>() as u32,
        hWnd: hwnd,
        uID: ICON_ID,
        uFlags: 0,
        uCallbackMessage: 0,
        hIcon: Hicon(0),
        szTip: [0u16; 128],
        dwState: 0,
        dwStateMask: 0,
        szInfo: [0u16; 256],
        uTimeoutOrVersion: 0,
        szInfoTitle: [0u16; 64],
        dwInfoFlags: 0,
        guidItem: [0u8; 16],
        hBalloonIcon: Hicon(0),
    }
}

fn add_icon(hwnd: Hwnd, icon: Hicon, tooltip: &str) -> anyhow::Result<()> {
    let mut data = notify_icon_data(hwnd);
    data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
    data.uCallbackMessage = CALLBACK_MESSAGE;
    data.hIcon = icon;

    let limited = limit_tooltip(tooltip);
    let mut tip_buf = [0u16; 128];
    let tip_wide: Vec<u16> = limited.encode_utf16().take(127).collect();
    let len = tip_wide.len().min(127);
    tip_buf[..len].copy_from_slice(&tip_wide[..len]);
    data.szTip = tip_buf;

    let ok = unsafe { Shell_NotifyIconW(NIM_ADD, &data) };
    if ok == 0 {
        anyhow::bail!("Failed to add tray icon.");
    }
    Ok(())
}

fn limit_tooltip(tooltip: &str) -> String {
    if tooltip.len() <= 127 {
        tooltip.to_string()
    } else {
        tooltip[..127].to_string()
    }
}
