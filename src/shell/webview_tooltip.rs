//! Custom tooltip rendered with WebView2 (via `wry`).
//!
//! Instead of approximating CodexBar's look, this hosts a real WebView2 control
//! and renders a tooltip-only subset of the Win-CodexBar tray styles. The DOM
//! is built in `assets/tooltip.js` with the matching `MenuCard.tsx` /
//! `TrayPanel.tsx` class hierarchy.
//!
//! The popup is a borderless, top-most, non-activating window. It is shown in
//! response to `NIN_POPUPOPEN` and hidden on `NIN_POPUPCLOSE`. Content is pushed
//! from Rust over `evaluate_script`; the rendered height comes back over the
//! wry IPC bridge so the host window can size itself to the content.

use std::cell::RefCell;
use std::num::NonZeroIsize;
use std::sync::atomic::{AtomicBool, AtomicI32, Ordering};
use std::sync::{Mutex, OnceLock};

use wry::raw_window_handle::{
    HandleError, HasWindowHandle, RawWindowHandle, Win32WindowHandle, WindowHandle,
};
use wry::WebViewBuilder;

use crate::domain::TooltipCard;
use crate::shell::native::{
    CoInitializeEx, CreateRoundRectRgn, CreateWindowExW, DefWindowProcW, GetDpiForWindow, Hmenu,
    Hwnd, PostMessageW, Rect, SetWindowPos, SetWindowRgn, ShowWindow, SystemParametersInfoW,
    COINIT_APARTMENTTHREADED, HWND_TOPMOST, SPI_GETWORKAREA, SWP_NOACTIVATE, SWP_NOMOVE,
    SWP_NOZORDER, SW_HIDE, SW_SHOWNOACTIVATE, WM_APP, WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW,
    WS_EX_TOPMOST, WS_POPUP,
};
use crate::shell::wide::{register_window_class, wide_nul};

/// Re-render request, posted to the popup when new content arrives.
const WM_TT_SETDATA: u32 = WM_APP + 3;

const WIDTH: i32 = 300;
const CORNER_RADIUS: i32 = 10;
const MIN_HEIGHT: i32 = 60;

/// Tooltip stylesheet + the DOM-building shim, embedded at build time.
const USAGEBAR_CSS: &str = include_str!("../../assets/usagebar.css");
const TOOLTIP_JS: &str = include_str!("../../assets/tooltip.js");

// ── Shared state (UI thread owns the WebView; statics bridge other threads) ─
static POPUP_HWND: OnceLock<Hwnd> = OnceLock::new();
static PENDING_JSON: OnceLock<Mutex<String>> = OnceLock::new();
static LAST_ICON_RECT: OnceLock<Mutex<Rect>> = OnceLock::new();
static HEIGHT: AtomicI32 = AtomicI32::new(0);
static VISIBLE: AtomicBool = AtomicBool::new(false);

thread_local! {
    /// The WebView is `!Send`; it is created and only ever touched on the tray
    /// message-loop (UI) thread.
    static WEBVIEW: RefCell<Option<wry::WebView>> = const { RefCell::new(None) };
}

fn pending() -> &'static Mutex<String> {
    PENDING_JSON.get_or_init(|| Mutex::new(r#"{"cards":[]}"#.to_string()))
}

fn last_icon_rect() -> &'static Mutex<Rect> {
    LAST_ICON_RECT.get_or_init(|| Mutex::new(Rect::default()))
}

/// Wraps our Win32 `HWND` so `wry` can attach the WebView to it.
struct WindowTarget(isize);

impl HasWindowHandle for WindowTarget {
    fn window_handle(&self) -> Result<WindowHandle<'_>, HandleError> {
        let hwnd = NonZeroIsize::new(self.0).ok_or(HandleError::Unavailable)?;
        let handle = Win32WindowHandle::new(hwnd);
        // SAFETY: the HWND outlives every WebView use (process lifetime).
        Ok(unsafe { WindowHandle::borrow_raw(RawWindowHandle::Win32(handle)) })
    }
}

// ── Public API ─────────────────────────────────────────────────────────────

/// Creates the hidden popup window and its WebView. Must be called once from
/// the tray message-loop thread. Returns an error if WebView2 is unavailable.
pub fn init() -> anyhow::Result<()> {
    if POPUP_HWND.get().is_some() {
        return Ok(());
    }

    // WebView2 requires an STA, message-pumping thread.
    unsafe {
        CoInitializeEx(std::ptr::null(), COINIT_APARTMENTTHREADED);
    }

    let (class_name, instance) = register_window_class(tooltip_proc, "UsageBarRustWebTooltip")?;

    let window_name = wide_nul("UsageBarRustWebTooltip");
    let hwnd = unsafe {
        CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
            class_name.as_ptr(),
            window_name.as_ptr(),
            WS_POPUP,
            0,
            0,
            WIDTH,
            MIN_HEIGHT,
            Hwnd(0),
            Hmenu(0),
            instance,
            std::ptr::null(),
        )
    };
    if hwnd.0 == 0 {
        anyhow::bail!(
            "Failed to create tooltip window. Error: {}",
            std::io::Error::last_os_error()
        );
    }

    // Size the window to WIDTH *logical* pixels before attaching the WebView so
    // the page always lays out at WIDTH CSS px (height measurements then stay
    // DPI-independent).
    let scale = window_scale(hwnd);
    unsafe {
        SetWindowPos(
            hwnd,
            0,
            0,
            0,
            scaled(WIDTH, scale),
            scaled(MIN_HEIGHT, scale),
            SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOZORDER,
        );
    }

    let html = build_html();
    let target = WindowTarget(hwnd.0);
    let webview = WebViewBuilder::new()
        .with_transparent(false)
        .with_html(html)
        .with_ipc_handler(|req: wry::http::Request<String>| handle_ipc(req.body()))
        .build(&target)?;

    WEBVIEW.with(|cell| *cell.borrow_mut() = Some(webview));
    let _ = POPUP_HWND.set(hwnd);
    Ok(())
}

/// Replaces the tooltip content. Thread-safe; the actual `evaluate_script`
/// happens on the UI thread via a posted message.
pub fn set_content(cards: Vec<TooltipCard>) {
    let json = serde_json::to_string(&cards).unwrap_or_else(|_| "[]".to_string());
    {
        let mut guard = pending().lock().unwrap();
        *guard = format!(r#"{{"cards":{json}}}"#);
    }
    if let Some(&hwnd) = POPUP_HWND.get() {
        unsafe {
            PostMessageW(hwnd, WM_TT_SETDATA, 0, 0);
        }
    }
}

/// Shows the popup anchored directly above the tray icon (like the native
/// tooltip). `icon_rect` is the icon's screen rectangle when known.
pub fn show_near_icon(icon_rect: Option<Rect>, fallback_x: i32, fallback_y: i32) {
    if POPUP_HWND.get().is_none() {
        return;
    }
    let rect = icon_rect.unwrap_or(Rect {
        left: fallback_x - 8,
        top: fallback_y - 8,
        right: fallback_x + 8,
        bottom: fallback_y + 8,
    });
    {
        let mut guard = last_icon_rect().lock().unwrap();
        *guard = rect;
    }
    VISIBLE.store(true, Ordering::SeqCst);
    reposition();
    if let Some(&hwnd) = POPUP_HWND.get() {
        unsafe {
            ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        }
    }
}

/// Hides the popup.
pub fn hide() {
    VISIBLE.store(false, Ordering::SeqCst);
    if let Some(&hwnd) = POPUP_HWND.get() {
        unsafe {
            ShowWindow(hwnd, SW_HIDE);
        }
    }
}

// ── Internal ────────────────────────────────────────────────────────────────

fn build_html() -> String {
    format!(
        "<!doctype html><html data-theme=\"dark\"><head><meta charset=\"utf-8\">\
<style>{USAGEBAR_CSS}</style></head>\
<body><div id=\"root\"></div><script>{TOOLTIP_JS}</script></body></html>"
    )
}

/// Handles IPC messages posted by `tooltip.js`.
fn handle_ipc(body: &str) {
    let Ok(value) = serde_json::from_str::<serde_json::Value>(body) else {
        return;
    };
    match value.get("type").and_then(|t| t.as_str()) {
        // The page finished loading — push the current snapshot.
        Some("ready") => {
            if let Some(&hwnd) = POPUP_HWND.get() {
                unsafe {
                    PostMessageW(hwnd, WM_TT_SETDATA, 0, 0);
                }
            }
        }
        // The page reported its rendered height — size the window to it.
        Some("height") => {
            if let Some(h) = value.get("value").and_then(|v| v.as_i64()) {
                HEIGHT.store((h as i32).max(MIN_HEIGHT), Ordering::SeqCst);
                if VISIBLE.load(Ordering::SeqCst) {
                    reposition();
                }
            }
        }
        _ => {}
    }
}

/// Scales a logical (CSS) pixel length by the monitor DPI factor.
fn scaled(value: i32, scale: f64) -> i32 {
    (value as f64 * scale).round() as i32
}

/// Reads the DPI scale factor for the window (1.0 at 96 DPI / 100%).
fn window_scale(hwnd: Hwnd) -> f64 {
    let dpi = unsafe { GetDpiForWindow(hwnd) };
    if dpi == 0 {
        1.0
    } else {
        dpi as f64 / 96.0
    }
}

/// Positions and sizes the popup above the tray icon, clamped to the work area.
///
/// `HEIGHT` is the content height in CSS pixels; window/screen coordinates are
/// physical pixels, so logical sizes are scaled by the monitor DPI.
fn reposition() {
    let Some(&hwnd) = POPUP_HWND.get() else {
        return;
    };
    let scale = window_scale(hwnd);
    let height_css = HEIGHT.load(Ordering::SeqCst).max(MIN_HEIGHT);
    let width = scaled(WIDTH, scale);
    let height = scaled(height_css, scale);
    let gap = scaled(8, scale);
    let radius = scaled(CORNER_RADIUS, scale);

    let icon = *last_icon_rect().lock().unwrap();
    let work = work_area();

    let mut x = icon.right - width;
    let mut y = icon.top - height - gap;
    if y < work.top {
        y = icon.bottom + gap;
    }
    if x + width > work.right {
        x = work.right - width - 4;
    }
    if x < work.left {
        x = work.left + 4;
    }
    if y + height > work.bottom {
        y = work.bottom - height - 4;
    }
    if y < work.top {
        y = work.top + 4;
    }

    unsafe {
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE);
        // SetWindowRgn takes ownership of the region; do not delete it.
        let region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius, radius);
        SetWindowRgn(hwnd, region, 1);
    }
}

fn work_area() -> Rect {
    let mut rect = Rect::default();
    let ok = unsafe {
        SystemParametersInfoW(
            SPI_GETWORKAREA,
            0,
            &mut rect as *mut Rect as *mut std::ffi::c_void,
            0,
        )
    };
    if ok == 0 || rect.right <= rect.left || rect.bottom <= rect.top {
        return Rect {
            left: 0,
            top: 0,
            right: 1920,
            bottom: 1040,
        };
    }
    rect
}

extern "system" fn tooltip_proc(hwnd: Hwnd, msg: u32, wparam: usize, lparam: isize) -> isize {
    if msg == WM_TT_SETDATA {
        let json = pending().lock().unwrap().clone();
        WEBVIEW.with(|cell| {
            if let Some(webview) = cell.borrow().as_ref() {
                let _ =
                    webview.evaluate_script(&format!("window.__render && window.__render({json})"));
            }
        });
        return 0;
    }
    unsafe { DefWindowProcW(hwnd, msg, wparam, lparam) }
}
