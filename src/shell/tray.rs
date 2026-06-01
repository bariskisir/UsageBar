use std::sync::{Arc, Mutex};
use tokio::sync::mpsc;

use crate::domain::{UsageBarWindow, UsageBlock};
use crate::shell::native::{
    AppendMenuW, CreatePopupMenu, CreateWindowExW, DefWindowProcW, DestroyIcon, DestroyMenu,
    DestroyWindow, DispatchMessageW, GetCursorPos, GetMessageW, Hicon, Hmenu, Hwnd, Msg,
    NotifyIconDataW, NotifyIconIdentifier, Point, PostMessageW, PostQuitMessage, Rect,
    SetForegroundWindow, Shell_NotifyIconGetRect, Shell_NotifyIconW, TrackPopupMenuEx,
    TranslateMessage, MF_CHECKED, MF_POPUP, MF_SEPARATOR, MF_STRING, NIF_ICON, NIF_INFO,
    NIF_MESSAGE, NIF_SHOWTIP, NIF_TIP, NIIF_ERROR, NIIF_INFO, NIIF_WARNING, NIM_ADD, NIM_DELETE,
    NIM_MODIFY, NIM_SETVERSION,
    NIN_POPUPCLOSE, NIN_POPUPOPEN, NOTIFYICON_VERSION_4, TPM_RETURNCMD, TPM_RIGHTBUTTON, WM_APP,
    WM_CONTEXTMENU, WM_DESTROY, WM_NULL, WM_RBUTTONUP,
};
use crate::shell::wide::{register_window_class, to_wide_array, wide_nul};

const ICON_ID: u32 = 1;
const CALLBACK_MESSAGE: u32 = WM_APP + 1;

// Right-click context menu command IDs.
const REFRESH_NOW_ID: u32 = 1001;
const EXIT_ID: u32 = 1002;

// "Refresh every" submenu: 2001–2004.
const REFRESH_EVERY_BASE: u32 = 2001;
const REFRESH_EVERY_VALUES: &[i32] = &[1, 5, 15, 60];

// "High Level" submenu: 3001–3009.
const HIGH_LEVEL_BASE: u32 = 3001;
// "Critical Level" submenu: 4001–4009.
const CRITICAL_LEVEL_BASE: u32 = 4001;
const LEVEL_VALUES: &[i32] = &[10, 20, 30, 40, 50, 60, 70, 80, 90];

// Settings service is placed here so the window proc can read/write settings
// synchronously when menu items are selected.
static SETTINGS_SVC: std::sync::OnceLock<
    std::sync::Mutex<Option<Arc<crate::infrastructure::settings::SettingsService>>>,
> = std::sync::OnceLock::new();

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

/// Severity of a threshold notification, selecting the balloon glyph.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum NotificationLevel {
    /// Usage dropped — positive event, shows the custom success (green check) symbol.
    Reset,
    /// Crossed the high threshold — shows the info symbol.
    High,
    /// Crossed the critical threshold — shows the critical (error) symbol.
    Critical,
}

/// A system-tray icon backed by a hidden Win32 window.
///
/// All public methods use `&self` (interior mutability) so the instance can be
/// shared between threads.  `Hwnd` and `Hicon` are explicitly `Send + Sync`.
pub struct TrayIcon {
    window_handle: Hwnd,
    icon_handle: Mutex<Hicon>,
    /// When `true`, the native Win32 tooltip is used; otherwise usage is shown
    /// in the custom [`crate::shell::webview_tooltip`] popup.
    use_legacy_tooltip: bool,
}

// SAFETY: Win32 handles (HWND, HICON) are safe to send and share between
// threads.  The Mutex provides safe interior mutability for the icon handle.
unsafe impl Send for TrayIcon {}
unsafe impl Sync for TrayIcon {}

impl TrayIcon {
    /// Creates the hidden window, registers the tray icon, and returns both the
    /// `TrayIcon` and the receiver for tray events.
    ///
    /// When `use_legacy_tooltip` is `false`, the custom tooltip window is
    /// initialised and the tray icon is switched to `NOTIFYICON_VERSION_4` so
    /// the shell delivers `NIN_POPUPOPEN` / `NIN_POPUPCLOSE` hover events.
    pub fn new(
        use_legacy_tooltip: bool,
        settings_svc: Arc<crate::infrastructure::settings::SettingsService>,
    ) -> anyhow::Result<(Self, mpsc::UnboundedReceiver<TrayEvent>)> {
        let (tx, rx) = mpsc::unbounded_channel();
        EVENT_TX
            .set(Mutex::new(Some(tx)))
            .map_err(|_| anyhow::anyhow!("TrayIcon already initialised."))?;

        let (class_name, instance) = register_window_class(window_proc, "UsageBarRustTrayWindow")?;

        let window_name = wide_nul("Usage Bar");

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
            anyhow::bail!(
                "Failed to create tray window. Error: {}",
                std::io::Error::last_os_error()
            );
        }

        // In custom-tooltip mode, create the WebView popup before registering
        // the icon so it is ready by the time the first hover arrives. If
        // WebView2 is unavailable, fall back to the native tooltip.
        let mut effective_legacy = use_legacy_tooltip;
        if !effective_legacy {
            if let Err(_err) = crate::shell::webview_tooltip::init() {
                effective_legacy = true;
            }
        }

        let icon_handle = Mutex::new(crate::shell::icon::create_usage_icon(&[], &[])?);
        add_icon(
            window_handle,
            *icon_handle.lock().unwrap(),
            "UsageBarRust\nLoading...",
            effective_legacy,
        )?;

        // Make the settings service available from the window proc (the proc
        // runs on the main thread, so it can read/write synchronously).
        let _ = SETTINGS_SVC.set(Mutex::new(Some(Arc::clone(&settings_svc))));

        Ok((
            Self {
                window_handle,
                icon_handle,
                use_legacy_tooltip: effective_legacy,
            },
            rx,
        ))
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

    /// Updates the tooltip from the current usage snapshot.
    ///
    /// In legacy mode this writes the native, 127-character-capped
    /// `NOTIFYICONDATA.szTip`. Otherwise it feeds the custom tooltip window,
    /// which has no length limit.
    pub fn update_tooltip(
        &self,
        blocks: &[UsageBlock],
        windows: &[UsageBarWindow],
        plans: &[(String, String)],
    ) {
        if self.use_legacy_tooltip {
            let text = crate::application::tooltip::format(blocks);
            self.set_native_tip(&text);
        } else {
            let cards = crate::application::tooltip::build_cards(blocks, windows, plans);
            crate::shell::webview_tooltip::set_content(cards);
        }
    }

    /// Writes the native Win32 tray tooltip (thread-safe, truncates to 127 chars).
    fn set_native_tip(&self, tooltip: &str) {
        let mut data = notify_icon_data(self.window_handle);
        data.uFlags = NIF_TIP | NIF_SHOWTIP;
        data.szTip = to_wide_array::<128>(tooltip);

        unsafe {
            Shell_NotifyIconW(NIM_MODIFY, &data);
        }
    }

    /// Replaces the tray icon (thread-safe).
    pub fn update_icon(
        &self,
        windows: &[UsageBarWindow],
        plans: &[(String, String)],
    ) -> anyhow::Result<()> {
        let next_icon = crate::shell::icon::create_usage_icon(windows, plans)?;
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

    /// Shows a short Windows notification from the tray icon. The balloon glyph
    /// is one of Windows' built-in notification symbols, chosen from `level`:
    /// reset → info, high → warning, critical → error.
    pub fn show_notification(&self, level: NotificationLevel, message: &str) {
        let mut data = notify_icon_data(self.window_handle);
        data.uFlags = NIF_INFO;
        data.szInfo = to_wide_array(message);
        //data.szInfoTitle = to_wide_array::<64>("Usage Bar");
        data.dwInfoFlags = match level {
            NotificationLevel::Reset => NIIF_INFO,
            NotificationLevel::High => NIIF_WARNING,
            NotificationLevel::Critical => NIIF_ERROR,
        };

        unsafe {
            Shell_NotifyIconW(NIM_MODIFY, &data);
        }
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
        // With NOTIFYICON_VERSION_4 the notification event id is in the low
        // word of lParam; this is also true for the legacy mouse-message form.
        let event = (lparam as u64 & 0xffff) as u32;
        match event {
            WM_RBUTTONUP | WM_CONTEXTMENU => {
                let _ = show_context_menu(hwnd);
            }
            NIN_POPUPOPEN => {
                let rect = icon_rect(hwnd);
                let (x, y) = hover_anchor(wparam);
                crate::shell::webview_tooltip::show_near_icon(rect, x, y);
            }
            NIN_POPUPCLOSE => {
                crate::shell::webview_tooltip::hide();
            }
            _ => {}
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
    if unsafe { GetCursorPos(&mut point) } == 0 {
        return Ok(());
    }

    let menu = unsafe { CreatePopupMenu() };
    if menu.0 == 0 {
        return Ok(());
    }

    // Read current settings so we can check the active items.
    let settings = current_settings();

    append_submenu(
        menu,
        submenu_with_checked_int(
            REFRESH_EVERY_BASE,
            REFRESH_EVERY_VALUES,
            settings.refresh_period_minute,
            |v| format!("{} min", v),
        ),
        "Refresh every",
    );
    append_submenu(
        menu,
        submenu_with_checked_int(
            HIGH_LEVEL_BASE,
            LEVEL_VALUES,
            settings.high_percentage.round() as i32,
            |v| format!("{}%", v),
        ),
        "High Level",
    );
    append_submenu(
        menu,
        submenu_with_checked_int(
            CRITICAL_LEVEL_BASE,
            LEVEL_VALUES,
            settings.critical_percentage.round() as i32,
            |v| format!("{}%", v),
        ),
        "Critical Level",
    );

    append_separator(menu);
    append_item(menu, MF_STRING, REFRESH_NOW_ID, "Refresh");
    append_item(menu, MF_STRING, EXIT_ID, "Exit");

    let cmd = unsafe {
        SetForegroundWindow(hwnd);
        let cmd = TrackPopupMenuEx(
            menu,
            TPM_RIGHTBUTTON | TPM_RETURNCMD,
            point.x,
            point.y,
            hwnd,
            std::ptr::null(),
        );
        // Per Win32 docs, post a benign message so the menu dismisses cleanly.
        PostMessageW(hwnd, WM_NULL, 0, 0);
        cmd
    };

    // Submenus are destroyed together with the parent menu.
    unsafe {
        DestroyMenu(menu);
    }

    if cmd != 0 {
        handle_menu_command(cmd as u32);
    }

    Ok(())
}

/// Appends a clickable string item with command id `id`.
fn append_item(menu: Hmenu, flags: u32, id: u32, text: &str) {
    let text = wide_nul(text);
    unsafe {
        AppendMenuW(menu, flags, id as usize, text.as_ptr());
    }
}

/// Appends `sub` as a labelled popup submenu of `menu`.
fn append_submenu(menu: Hmenu, sub: Hmenu, text: &str) {
    let text = wide_nul(text);
    unsafe {
        AppendMenuW(menu, MF_POPUP, sub.0 as usize, text.as_ptr());
    }
}

/// Appends a horizontal separator line.
fn append_separator(menu: Hmenu) {
    unsafe {
        AppendMenuW(menu, MF_SEPARATOR, 0, std::ptr::null());
    }
}

/// Reads the current `AppSettings` synchronously (this is the UI thread).
fn current_settings() -> crate::infrastructure::settings::AppSettings {
    SETTINGS_SVC
        .get()
        .and_then(|lock| lock.lock().ok())
        .and_then(|guard| guard.as_ref().map(|svc| svc.read_sync()))
        .unwrap_or_default()
}

/// Writes updated settings synchronously and fires a refresh event so the
/// coordinator wakes up with the new period.
fn apply_settings(settings: &crate::infrastructure::settings::AppSettings) {
    if let Some(lock) = SETTINGS_SVC.get() {
        if let Ok(guard) = lock.lock() {
            if let Some(svc) = guard.as_ref() {
                let _ = svc.write_sync(settings);
            }
        }
    }
    send_event(TrayEvent::Refresh);
}

/// Routes a menu command id to the appropriate action.
fn handle_menu_command(cmd: u32) {
    match cmd {
        REFRESH_NOW_ID => send_event(TrayEvent::Refresh),
        EXIT_ID => {
            send_event(TrayEvent::Exit);
            unsafe { PostQuitMessage(0) };
        }
        _ => {
            let _ = apply_ranged_setting(cmd, REFRESH_EVERY_BASE, REFRESH_EVERY_VALUES, |s, v| {
                s.refresh_period_minute = v;
            }) || apply_ranged_setting(cmd, HIGH_LEVEL_BASE, LEVEL_VALUES, |s, v| {
                s.high_percentage = v as f64;
            }) || apply_ranged_setting(cmd, CRITICAL_LEVEL_BASE, LEVEL_VALUES, |s, v| {
                s.critical_percentage = v as f64;
            });
        }
    }
}

/// If `cmd` selects an item from the `[base, base + values.len())` submenu,
/// reads the current settings, applies `set` with the chosen value, persists,
/// and returns `true`. Returns `false` when `cmd` is out of range.
fn apply_ranged_setting(
    cmd: u32,
    base: u32,
    values: &[i32],
    set: impl FnOnce(&mut crate::infrastructure::settings::AppSettings, i32),
) -> bool {
    if cmd < base || cmd >= base + values.len() as u32 {
        return false;
    }
    let value = values[(cmd - base) as usize];
    let mut s = current_settings();
    set(&mut s, value);
    apply_settings(&s);
    true
}

/// Builds a submenu with integer-option items. The one matching `selected`
/// gets a check-mark.
fn submenu_with_checked_int(
    base_id: u32,
    values: &[i32],
    selected: i32,
    label_for: impl Fn(i32) -> String,
) -> Hmenu {
    let hmenu = unsafe { CreatePopupMenu() };
    if hmenu.0 == 0 {
        return hmenu;
    }
    for (i, &v) in values.iter().enumerate() {
        let id = (base_id + i as u32) as usize;
        let text: Vec<u16> = format!("{}\0", label_for(v)).encode_utf16().collect();
        let flags = if v == selected {
            MF_STRING | MF_CHECKED
        } else {
            MF_STRING
        };
        unsafe {
            AppendMenuW(hmenu, flags, id, text.as_ptr());
        }
    }
    hmenu
}

fn send_event(event: TrayEvent) {
    let lock = EVENT_TX.get().and_then(|m| m.lock().ok());
    if let Some(guard) = lock {
        if let Some(ref tx) = *guard {
            let _ = tx.send(event);
        }
    }
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

fn add_icon(hwnd: Hwnd, icon: Hicon, tooltip: &str, legacy: bool) -> anyhow::Result<()> {
    let mut data = notify_icon_data(hwnd);
    // In custom-tooltip mode we keep NIF_TIP (so the shell tracks hover and
    // sends NIN_POPUPOPEN) but omit NIF_SHOWTIP so the standard tooltip never
    // appears — our own popup is shown instead.
    data.uFlags = if legacy {
        NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP
    } else {
        NIF_MESSAGE | NIF_ICON | NIF_TIP
    };
    data.uCallbackMessage = CALLBACK_MESSAGE;
    data.hIcon = icon;
    data.szTip = to_wide_array::<128>(tooltip);

    let ok = unsafe { Shell_NotifyIconW(NIM_ADD, &data) };
    if ok == 0 {
        anyhow::bail!("Failed to add tray icon.");
    }

    // Opt into version 4 so the shell delivers NIN_POPUPOPEN / NIN_POPUPCLOSE
    // hover notifications for the custom tooltip.
    if !legacy {
        let mut version = notify_icon_data(hwnd);
        version.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        unsafe {
            Shell_NotifyIconW(NIM_SETVERSION, &version);
        }
    }

    Ok(())
}

/// Returns the tray icon's bounding rectangle in screen coordinates, so the
/// custom tooltip can be anchored directly above it (like the native tooltip).
/// Returns `None` if the shell can't resolve the icon position.
fn icon_rect(hwnd: Hwnd) -> Option<Rect> {
    let identifier = NotifyIconIdentifier {
        cbSize: std::mem::size_of::<NotifyIconIdentifier>() as u32,
        hWnd: hwnd,
        uID: ICON_ID,
        guidItem: [0u8; 16],
    };
    let mut rect = Rect::default();
    let hr = unsafe { Shell_NotifyIconGetRect(&identifier, &mut rect) };
    if hr == 0 && rect.right > rect.left && rect.bottom > rect.top {
        Some(rect)
    } else {
        None
    }
}

/// Resolves the screen-space anchor for a hover popup. With
/// `NOTIFYICON_VERSION_4`, `wParam` carries the anchor point (x in the low
/// word, y in the high word). Falls back to the cursor position when those
/// coordinates are unavailable.
fn hover_anchor(wparam: usize) -> (i32, i32) {
    let x = (wparam & 0xffff) as u16 as i16 as i32;
    let y = ((wparam >> 16) & 0xffff) as u16 as i16 as i32;
    if x != 0 || y != 0 {
        return (x, y);
    }

    let mut point = Point { x: 0, y: 0 };
    if unsafe { GetCursorPos(&mut point) } != 0 {
        (point.x, point.y)
    } else {
        (x, y)
    }
}
