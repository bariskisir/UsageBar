#![allow(unsafe_op_in_unsafe_fn)]

use std::{
    ffi::c_void,
    mem::{size_of, zeroed},
    num::NonZeroIsize,
    ptr::{null, null_mut},
    sync::{
        Arc, Mutex, OnceLock,
        atomic::{AtomicPtr, Ordering},
    },
};

use anyhow::{Result, anyhow};
use serde_json::json;
use tokio::runtime::Handle;
use windows_sys::Win32::{
    Foundation::{HWND, LPARAM, LRESULT, POINT, RECT, WPARAM},
    Graphics::Gdi::{CreateRoundRectRgn, HBRUSH, SetWindowRgn},
    System::LibraryLoader::GetModuleHandleW,
    UI::{
        HiDpi::{
            DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2, GetDpiForWindow,
            SetProcessDpiAwarenessContext,
        },
        Shell::{NOTIFYICONDATAW, Shell_NotifyIconW},
        Shell::{NOTIFYICONIDENTIFIER, Shell_NotifyIconGetRect},
        WindowsAndMessaging::{
            AppendMenuW, CreateIcon, CreatePopupMenu, CreateWindowExW, DefWindowProcW, DestroyIcon,
            DestroyMenu, DestroyWindow, DispatchMessageW, GetCursorPos, GetMessageW, HICON,
            HWND_TOPMOST, MSG, PostMessageW, PostQuitMessage, RegisterClassW, SPI_GETWORKAREA,
            SW_HIDE, SW_SHOWNOACTIVATE, SWP_NOACTIVATE, SWP_NOMOVE, SWP_NOZORDER,
            SetForegroundWindow, SetWindowPos, ShowWindow, SystemParametersInfoW, TPM_RETURNCMD,
            TPM_RIGHTBUTTON, TrackPopupMenu, TranslateMessage, WINDOW_EX_STYLE, WINDOW_STYLE,
            WM_APP, WM_CONTEXTMENU, WM_DESTROY, WM_NULL, WM_RBUTTONUP, WNDCLASSW, WS_EX_NOACTIVATE,
            WS_EX_TOOLWINDOW, WS_EX_TOPMOST, WS_OVERLAPPED, WS_POPUP,
        },
    },
};
use winreg::{RegKey, enums::HKEY_CURRENT_USER};
use wry::{
    Rect as WebViewRect, WebView, WebViewBuilder, dpi,
    raw_window_handle::{
        HandleError, HasWindowHandle, RawWindowHandle, Win32WindowHandle, WindowHandle,
    },
};

use crate::{
    app::{IconBar, IconLayout, UsageRefreshService, UsageView},
    config::{AppPaths, AppSettings, JsonSettingsStore},
    domain::{NotificationLevel, ProviderPlan, TooltipCard, UsageWindow},
    providers::{http_client, providers},
};

const CALLBACK_MESSAGE: u32 = WM_APP + 1;
const MANUAL_REFRESH_MESSAGE: u32 = WM_APP + 2;
const TOOLTIP_SET_DATA_MESSAGE: u32 = WM_APP + 3;
const TOOLTIP_HIDE_MESSAGE: u32 = WM_APP + 5;
const TOOLTIP_RESIZE_MESSAGE: u32 = WM_APP + 6;

const ICON_ID: u32 = 1;
const REFRESH_COMMAND: u32 = 1001;
const EXIT_COMMAND: u32 = 1002;
const REFRESH_EVERY_BASE: u32 = 2001;
const HIGH_LEVEL_BASE: u32 = 3001;
const CRITICAL_LEVEL_BASE: u32 = 4001;

const REFRESH_EVERY_VALUES: [u64; 4] = [1, 5, 15, 60];
const LEVEL_VALUES: [f64; 9] = [10.0, 20.0, 30.0, 40.0, 50.0, 60.0, 70.0, 80.0, 90.0];

const NIF_MESSAGE: u32 = 0x00000001;
const NIF_ICON: u32 = 0x00000002;
const NIF_TIP: u32 = 0x00000004;
const NIF_INFO: u32 = 0x00000010;
const NIM_ADD: u32 = 0x00000000;
const NIM_MODIFY: u32 = 0x00000001;
const NIM_DELETE: u32 = 0x00000002;
const NIM_SETVERSION: u32 = 0x00000004;
const NOTIFYICON_VERSION_4: u32 = 4;
const NIIF_INFO: u32 = 0x00000001;
const NIIF_WARNING: u32 = 0x00000002;
const NIIF_ERROR: u32 = 0x00000003;
const NIN_POPUPOPEN: u32 = 0x0406;
const NIN_POPUPCLOSE: u32 = 0x0407;

const MF_STRING: u32 = 0x00000000;
const MF_CHECKED: u32 = 0x00000008;
const MF_POPUP: u32 = 0x00000010;
const MF_SEPARATOR: u32 = 0x00000800;

const TOOLTIP_WIDTH: i32 = 300;
const TOOLTIP_INITIAL_HEIGHT: i32 = 260;
const TOOLTIP_MIN_HEIGHT: i32 = 60;
const TOOLTIP_CORNER_RADIUS: i32 = 10;
const TOOLTIP_GAP: i32 = 8;

const TRAY_ICON_SIZE: usize = 32;
const TRAY_ICON_SIZE_I32: i32 = 32;
const TRAY_ICON_PLATE_INSET: i32 = 2;
const TRAY_ICON_BAR_LEFT: i32 = 4;
const TRAY_ICON_BAR_RIGHT: i32 = TRAY_ICON_SIZE_I32 - 4;
const TRAY_ICON_CONTENT_TOP: i32 = 6;
const TRAY_ICON_CONTENT_BOTTOM: i32 = TRAY_ICON_SIZE_I32 - 6;
const TRAY_ICON_SEP_SAME_PROVIDER: i32 = 1;
const TRAY_ICON_SEP_CROSS_PROVIDER: i32 = 2;

static STATE: OnceLock<Arc<TrayState>> = OnceLock::new();
static TOOLTIP: AtomicPtr<TooltipWindow> = AtomicPtr::new(null_mut());

pub fn run_usagebar() -> Result<()> {
    unsafe {
        let _ = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }

    let paths = AppPaths::resolve()?;
    std::fs::create_dir_all(&paths.app_data_dir)?;
    let _logging_guard = init_logging(&paths)?;

    let settings = JsonSettingsStore::new(paths.settings_file.clone())?;
    let client = http_client()?;
    let providers = providers(client);

    let runtime = Handle::current();
    let view = Arc::new(TrayView::new()?);
    let service = Arc::new(UsageRefreshService::new(
        providers,
        settings.clone(),
        view.clone(),
    ));
    let state = Arc::new(TrayState {
        runtime,
        refresh: service,
        settings,
    });
    STATE
        .set(state.clone())
        .map_err(|_| anyhow!("tray state was already initialized"))?;

    ensure_startup_registered()?;
    state.refresh.clone().start();

    unsafe { message_loop() };
    Ok(())
}

fn init_logging(paths: &AppPaths) -> Result<tracing_appender::non_blocking::WorkerGuard> {
    let file_appender = tracing_appender::rolling::never(&paths.app_data_dir, "app.log");
    let (writer, guard) = tracing_appender::non_blocking(file_appender);
    let _ = tracing_subscriber::fmt()
        .with_writer(writer)
        .with_ansi(false)
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| "usagebar=info,warn".into()),
        )
        .try_init();
    Ok(guard)
}

struct TrayState {
    runtime: Handle,
    refresh: Arc<UsageRefreshService<TrayView>>,
    settings: JsonSettingsStore,
}

pub struct TrayView {
    hwnd: HWND,
    icon: AtomicPtr<c_void>,
    tooltip_hwnd: HWND,
    pending_json: Arc<Mutex<String>>,
    last_tip: Mutex<String>,
}

unsafe impl Send for TrayView {}
unsafe impl Sync for TrayView {}
unsafe impl Send for TrayState {}
unsafe impl Sync for TrayState {}

impl TrayView {
    fn new() -> Result<Self> {
        unsafe {
            let class = wide("UsageBarTrayWindow");
            let instance = GetModuleHandleW(null());
            let wc = WNDCLASSW {
                style: 0,
                lpfnWndProc: Some(wnd_proc),
                hInstance: instance,
                lpszClassName: class.as_ptr(),
                hbrBackground: null_mut::<c_void>() as HBRUSH,
                ..zeroed()
            };
            if RegisterClassW(&wc) == 0 {
                return Err(anyhow!("failed to register tray window class"));
            }

            let hwnd = CreateWindowExW(
                0 as WINDOW_EX_STYLE,
                class.as_ptr(),
                wide("UsageBar").as_ptr(),
                WS_OVERLAPPED as WINDOW_STYLE,
                0,
                0,
                0,
                0,
                null_mut(),
                null_mut(),
                instance,
                null(),
            );
            if hwnd.is_null() {
                return Err(anyhow!("failed to create tray window"));
            }

            let icon = create_usage_icon(&[], &[])?;
            add_icon(hwnd, icon, "Usage Bar")?;

            let pending_json = Arc::new(Mutex::new(r#"{"cards":[]}"#.to_string()));
            let tooltip = TooltipWindow::new(pending_json.clone())?;
            let tooltip_hwnd = tooltip.hwnd;
            TOOLTIP.store(Box::into_raw(Box::new(tooltip)), Ordering::SeqCst);

            Ok(Self {
                hwnd,
                icon: AtomicPtr::new(icon),
                tooltip_hwnd,
                pending_json,
                last_tip: Mutex::new("UsageBar".to_string()),
            })
        }
    }
}

impl UsageView for TrayView {
    fn show_icon(&self, windows: &[UsageWindow], plans: &[ProviderPlan]) {
        let bars = IconLayout::compute(windows, plans);
        let next_icon = unsafe { render_usage_icon(&bars) };
        let tip = if windows.is_empty() {
            "UsageBar - no provider data".to_string()
        } else {
            windows
                .iter()
                .map(|window| {
                    format!(
                        "{} {} {:.0}%",
                        window.provider_name, window.label, window.used_percent
                    )
                })
                .collect::<Vec<_>>()
                .join(" | ")
        };
        if let Ok(mut last_tip) = self.last_tip.lock() {
            *last_tip = tip.clone();
        }

        let tip = format!("UsageBar - {}", shorten(&tip, 110));
        match next_icon {
            Ok(next_icon) => unsafe {
                if let Err(error) = modify_tip(self.hwnd, next_icon, &tip) {
                    tracing::warn!(?error, "failed to update dynamic tray icon");
                    DestroyIcon(next_icon);
                } else {
                    let previous = self.icon.swap(next_icon, Ordering::SeqCst) as HICON;
                    if !previous.is_null() {
                        DestroyIcon(previous);
                    }
                }
            },
            Err(error) => {
                tracing::warn!(?error, "failed to render dynamic tray icon");
                let current = self.icon.load(Ordering::SeqCst) as HICON;
                let _ = unsafe { modify_tip(self.hwnd, current, &tip) };
            }
        }
        tracing::debug!(bars = bars.len(), "tray icon state updated");
    }

    fn show_cards(&self, cards: &[TooltipCard]) {
        let json = serde_json::to_string(&json!({ "cards": cards }))
            .unwrap_or_else(|_| r#"{"cards":[]}"#.to_string());
        if let Ok(mut pending) = self.pending_json.lock() {
            *pending = json;
        }
        unsafe {
            PostMessageW(self.tooltip_hwnd, TOOLTIP_SET_DATA_MESSAGE, 0, 0);
        }
    }

    fn notify(&self, level: NotificationLevel, message: &str) {
        let _ = unsafe { show_balloon(self.hwnd, level, message) };
    }
}

impl Drop for TrayView {
    fn drop(&mut self) {
        unsafe {
            let icon = self.icon.load(Ordering::SeqCst) as HICON;
            let data = notify_data(self.hwnd, icon, "");
            Shell_NotifyIconW(NIM_DELETE, &data);
            if !icon.is_null() {
                DestroyIcon(icon);
            }
            if !self.hwnd.is_null() {
                DestroyWindow(self.hwnd);
            }
        }
    }
}

struct TooltipWindow {
    hwnd: HWND,
    _webview: WebView,
    pending_json: Arc<Mutex<String>>,
    height_css: i32,
    anchor: Option<TooltipAnchor>,
    visible: bool,
}

#[derive(Clone, Copy)]
struct TooltipAnchor {
    icon_rect: RECT,
}

impl TooltipWindow {
    fn new(pending_json: Arc<Mutex<String>>) -> Result<Self> {
        unsafe {
            let class = wide("UsageBarHtmlTooltip");
            let instance = GetModuleHandleW(null());
            let wc = WNDCLASSW {
                style: 0,
                lpfnWndProc: Some(tooltip_wnd_proc),
                hInstance: instance,
                lpszClassName: class.as_ptr(),
                hbrBackground: null_mut::<c_void>() as HBRUSH,
                ..zeroed()
            };
            RegisterClassW(&wc);

            let hwnd = CreateWindowExW(
                (WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE) as WINDOW_EX_STYLE,
                class.as_ptr(),
                wide("UsageBarTooltip").as_ptr(),
                WS_POPUP as WINDOW_STYLE,
                0,
                0,
                TOOLTIP_WIDTH,
                TOOLTIP_INITIAL_HEIGHT,
                null_mut(),
                null_mut(),
                instance,
                null(),
            );
            if hwnd.is_null() {
                return Err(anyhow!("failed to create tooltip window"));
            }

            let scale = window_scale(hwnd);
            SetWindowPos(
                hwnd,
                null_mut(),
                0,
                0,
                scaled(TOOLTIP_WIDTH, scale),
                scaled(TOOLTIP_INITIAL_HEIGHT, scale),
                SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOZORDER,
            );

            let owner = HwndOwner(hwnd);
            let tooltip_hwnd = hwnd;
            let webview = WebViewBuilder::new()
                .with_transparent(false)
                .with_bounds(tooltip_webview_bounds(TOOLTIP_INITIAL_HEIGHT))
                .with_html(build_tooltip_html())
                .with_ipc_handler(move |message| {
                    let Ok(value) = serde_json::from_str::<serde_json::Value>(message.body())
                    else {
                        return;
                    };
                    match value.get("type").and_then(|v| v.as_str()) {
                        Some("ready") => {
                            PostMessageW(tooltip_hwnd, TOOLTIP_SET_DATA_MESSAGE, 0, 0);
                        }
                        Some("height") => {
                            if let Some(height) = value
                                .get("value")
                                .and_then(|v| v.as_i64())
                                .and_then(|v| i32::try_from(v).ok())
                            {
                                let height = height.max(TOOLTIP_MIN_HEIGHT);
                                PostMessageW(
                                    tooltip_hwnd,
                                    TOOLTIP_RESIZE_MESSAGE,
                                    height as usize,
                                    0,
                                );
                            }
                        }
                        _ => {}
                    }
                })
                .build_as_child(&owner)?;

            Ok(Self {
                hwnd,
                _webview: webview,
                pending_json,
                height_css: TOOLTIP_INITIAL_HEIGHT,
                anchor: None,
                visible: false,
            })
        }
    }

    fn render_pending(&self) {
        let json = self
            .pending_json
            .lock()
            .map(|pending| pending.clone())
            .unwrap_or_else(|_| r#"{"cards":[]}"#.to_string());
        let script = format!("window.__render && window.__render({json})");
        if let Err(error) = self._webview.evaluate_script(&script) {
            tracing::warn!(?error, "failed to render tooltip cards");
        }
    }

    fn resize_to_content(&mut self, height_css: i32) {
        self.height_css = height_css.max(TOOLTIP_MIN_HEIGHT);
        if let Err(error) = self
            ._webview
            .set_bounds(tooltip_webview_bounds(self.height_css))
        {
            tracing::warn!(?error, "failed to resize tooltip webview");
        }
        unsafe {
            if self.visible {
                self.position_near_anchor();
            } else {
                let scale = window_scale(self.hwnd);
                SetWindowPos(
                    self.hwnd,
                    null_mut(),
                    0,
                    0,
                    scaled(TOOLTIP_WIDTH, scale),
                    scaled(self.height_css, scale),
                    SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOZORDER,
                );
            }
        }
    }

    unsafe fn show_near_icon(&mut self, icon_rect: Option<RECT>, fallback_x: i32, fallback_y: i32) {
        let fallback = RECT {
            left: fallback_x - 8,
            top: fallback_y - 8,
            right: fallback_x + 8,
            bottom: fallback_y + 8,
        };
        self.anchor = Some(TooltipAnchor {
            icon_rect: icon_rect.unwrap_or(fallback),
        });
        self.visible = true;
        self.position_near_anchor();
        ShowWindow(self.hwnd, SW_SHOWNOACTIVATE);
        self.render_pending();
    }

    unsafe fn position_near_anchor(&self) {
        let Some(anchor) = self.anchor else {
            return;
        };
        let scale = window_scale(self.hwnd);
        let width = scaled(TOOLTIP_WIDTH, scale);
        let height = scaled(self.height_css, scale);
        let gap = scaled(TOOLTIP_GAP, scale);
        let radius = scaled(TOOLTIP_CORNER_RADIUS, scale);
        let work = work_area();

        let mut x = anchor.icon_rect.right - width;
        let mut y = anchor.icon_rect.top - height - gap;

        if y < work.top {
            y = anchor.icon_rect.bottom + gap;
        }
        if x + width > work.right {
            x = work.right - width - scaled(4, scale);
        }
        if x < work.left {
            x = work.left + scaled(4, scale);
        }
        if y + height > work.bottom {
            y = work.bottom - height - scaled(4, scale);
        }
        if y < work.top {
            y = work.top + scaled(4, scale);
        }

        SetWindowPos(self.hwnd, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE);

        let region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius, radius);
        SetWindowRgn(self.hwnd, region, 1);
    }

    unsafe fn hide(&mut self) {
        self.visible = false;
        ShowWindow(self.hwnd, SW_HIDE);
    }
}

struct HwndOwner(HWND);

impl HasWindowHandle for HwndOwner {
    fn window_handle(&self) -> Result<WindowHandle<'_>, HandleError> {
        let hwnd = NonZeroIsize::new(self.0 as isize).ok_or(HandleError::Unavailable)?;
        let handle = Win32WindowHandle::new(hwnd);
        Ok(unsafe { WindowHandle::borrow_raw(RawWindowHandle::Win32(handle)) })
    }
}

unsafe extern "system" fn wnd_proc(
    hwnd: HWND,
    message: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match message {
        CALLBACK_MESSAGE => {
            let event = (lparam as u32) & 0xffff;
            match event {
                WM_RBUTTONUP | WM_CONTEXTMENU => show_context_menu(hwnd),
                NIN_POPUPOPEN => {
                    let (x, y) = hover_anchor(wparam);
                    let rect = icon_rect(hwnd);
                    if let Some(tooltip) = tooltip_mut() {
                        tooltip.show_near_icon(rect, x, y);
                    }
                }
                NIN_POPUPCLOSE => {
                    if let Some(tooltip) = tooltip_mut() {
                        tooltip.hide();
                    }
                }
                _ => {}
            }
            0
        }
        MANUAL_REFRESH_MESSAGE => {
            if let Some(state) = STATE.get() {
                let refresh = state.refresh.clone();
                state.runtime.spawn(async move {
                    refresh.refresh_once().await;
                });
            }
            0
        }
        WM_DESTROY => {
            PostQuitMessage(0);
            0
        }
        _ => DefWindowProcW(hwnd, message, wparam, lparam),
    }
}

unsafe extern "system" fn tooltip_wnd_proc(
    hwnd: HWND,
    message: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match message {
        TOOLTIP_SET_DATA_MESSAGE => {
            if let Some(tooltip) = tooltip_mut() {
                tooltip.render_pending();
            }
            0
        }
        TOOLTIP_HIDE_MESSAGE => {
            if let Some(tooltip) = tooltip_mut() {
                tooltip.hide();
            }
            0
        }
        TOOLTIP_RESIZE_MESSAGE => {
            if let Some(tooltip) = tooltip_mut() {
                tooltip.resize_to_content(wparam as i32);
            }
            0
        }
        _ => DefWindowProcW(hwnd, message, wparam, lparam),
    }
}

fn tooltip_mut() -> Option<&'static mut TooltipWindow> {
    let ptr = TOOLTIP.load(Ordering::SeqCst);
    if ptr.is_null() {
        None
    } else {
        Some(unsafe { &mut *ptr })
    }
}

unsafe fn show_context_menu(hwnd: HWND) {
    let menu = CreatePopupMenu();
    if menu.is_null() {
        return;
    }

    let settings = STATE
        .get()
        .map(|state| state.settings.read_or_default())
        .unwrap_or_default();

    let refresh_menu = checked_submenu_u64(
        REFRESH_EVERY_BASE,
        &REFRESH_EVERY_VALUES,
        settings.refresh_period_minute,
        " min",
    );
    AppendMenuW(
        menu,
        MF_POPUP,
        refresh_menu as usize,
        wide("Refresh every").as_ptr(),
    );

    let high_menu = checked_submenu_f64(
        HIGH_LEVEL_BASE,
        &LEVEL_VALUES,
        settings.high_percentage,
        "%",
    );
    AppendMenuW(
        menu,
        MF_POPUP,
        high_menu as usize,
        wide("High Level").as_ptr(),
    );

    let critical_menu = checked_submenu_f64(
        CRITICAL_LEVEL_BASE,
        &LEVEL_VALUES,
        settings.critical_percentage,
        "%",
    );
    AppendMenuW(
        menu,
        MF_POPUP,
        critical_menu as usize,
        wide("Critical Level").as_ptr(),
    );

    AppendMenuW(menu, MF_SEPARATOR, 0, null());
    AppendMenuW(
        menu,
        MF_STRING,
        REFRESH_COMMAND as usize,
        wide("Refresh").as_ptr(),
    );
    AppendMenuW(
        menu,
        MF_STRING,
        EXIT_COMMAND as usize,
        wide("Exit").as_ptr(),
    );

    let mut point = POINT { x: 0, y: 0 };
    GetCursorPos(&mut point);
    SetForegroundWindow(hwnd);
    let command = TrackPopupMenu(
        menu,
        TPM_RIGHTBUTTON | TPM_RETURNCMD,
        point.x,
        point.y,
        0,
        hwnd,
        null::<RECT>(),
    );
    DestroyMenu(menu);

    match command as u32 {
        REFRESH_COMMAND => {
            PostMessageW(hwnd, MANUAL_REFRESH_MESSAGE, 0, 0);
        }
        EXIT_COMMAND => {
            PostQuitMessage(0);
        }
        id if (REFRESH_EVERY_BASE..REFRESH_EVERY_BASE + REFRESH_EVERY_VALUES.len() as u32)
            .contains(&id) =>
        {
            apply_settings_change(hwnd, |settings| {
                settings.refresh_period_minute =
                    REFRESH_EVERY_VALUES[(id - REFRESH_EVERY_BASE) as usize];
            });
        }
        id if (HIGH_LEVEL_BASE..HIGH_LEVEL_BASE + LEVEL_VALUES.len() as u32).contains(&id) => {
            apply_settings_change(hwnd, |settings| {
                settings.high_percentage = LEVEL_VALUES[(id - HIGH_LEVEL_BASE) as usize];
            });
        }
        id if (CRITICAL_LEVEL_BASE..CRITICAL_LEVEL_BASE + LEVEL_VALUES.len() as u32)
            .contains(&id) =>
        {
            apply_settings_change(hwnd, |settings| {
                settings.critical_percentage = LEVEL_VALUES[(id - CRITICAL_LEVEL_BASE) as usize];
            });
        }
        _ => {}
    }
    PostMessageW(hwnd, WM_NULL, 0, 0);
}

unsafe fn checked_submenu_u64(base: u32, values: &[u64], selected: u64, suffix: &str) -> HWND {
    let menu = CreatePopupMenu();
    for (index, value) in values.iter().enumerate() {
        let flags = if *value == selected {
            MF_STRING | MF_CHECKED
        } else {
            MF_STRING
        };
        AppendMenuW(
            menu,
            flags,
            (base + index as u32) as usize,
            wide(&format!("{value}{suffix}")).as_ptr(),
        );
    }
    menu
}

unsafe fn checked_submenu_f64(base: u32, values: &[f64], selected: f64, suffix: &str) -> HWND {
    let menu = CreatePopupMenu();
    for (index, value) in values.iter().enumerate() {
        let display = *value as i32;
        let flags = if (selected - *value).abs() < f64::EPSILON {
            MF_STRING | MF_CHECKED
        } else {
            MF_STRING
        };
        AppendMenuW(
            menu,
            flags,
            (base + index as u32) as usize,
            wide(&format!("{display}{suffix}")).as_ptr(),
        );
    }
    menu
}

fn apply_settings_change(hwnd: HWND, change: impl FnOnce(&mut AppSettings)) {
    if let Some(state) = STATE.get() {
        let mut settings = state.settings.read_or_default();
        change(&mut settings);
        if let Err(error) = state.settings.write(&settings) {
            tracing::warn!(?error, "failed to write settings from tray menu");
        }
        unsafe {
            PostMessageW(hwnd, MANUAL_REFRESH_MESSAGE, 0, 0);
        }
    }
}

unsafe fn message_loop() {
    let mut message: MSG = zeroed();
    while GetMessageW(&mut message, null_mut(), 0, 0) > 0 {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
}

unsafe fn add_icon(hwnd: HWND, icon: HICON, tip: &str) -> Result<()> {
    let mut data = notify_data(hwnd, icon, tip);
    data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    data.uCallbackMessage = CALLBACK_MESSAGE;
    if Shell_NotifyIconW(NIM_ADD, &data) == 0 {
        return Err(anyhow!("failed to add notification area icon"));
    }
    data.uFlags = 0;
    data.Anonymous.uVersion = NOTIFYICON_VERSION_4;
    Shell_NotifyIconW(NIM_SETVERSION, &data);
    Ok(())
}

unsafe fn modify_tip(hwnd: HWND, icon: HICON, tip: &str) -> Result<()> {
    let mut data = notify_data(hwnd, icon, tip);
    data.uFlags = NIF_ICON | NIF_TIP;
    if Shell_NotifyIconW(NIM_MODIFY, &data) == 0 {
        return Err(anyhow!("failed to update notification area icon"));
    }
    Ok(())
}

unsafe fn show_balloon(hwnd: HWND, level: NotificationLevel, message: &str) -> Result<()> {
    let mut data = notify_data(hwnd, null_mut(), "");
    data.uFlags = NIF_INFO;
    copy_wide(&mut data.szInfo, &shorten(message, 255));
    data.dwInfoFlags = match level {
        NotificationLevel::Reset => NIIF_INFO,
        NotificationLevel::High => NIIF_WARNING,
        NotificationLevel::Critical => NIIF_ERROR,
    };
    if Shell_NotifyIconW(NIM_MODIFY, &data) == 0 {
        return Err(anyhow!("failed to show notification balloon"));
    }
    Ok(())
}

fn notify_data(hwnd: HWND, icon: HICON, tip: &str) -> NOTIFYICONDATAW {
    let mut data: NOTIFYICONDATAW = unsafe { zeroed() };
    data.cbSize = size_of::<NOTIFYICONDATAW>() as u32;
    data.hWnd = hwnd;
    data.uID = ICON_ID;
    data.hIcon = icon;
    copy_wide(&mut data.szTip, tip);
    data
}

unsafe fn icon_rect(hwnd: HWND) -> Option<RECT> {
    let identifier = NOTIFYICONIDENTIFIER {
        cbSize: size_of::<NOTIFYICONIDENTIFIER>() as u32,
        hWnd: hwnd,
        uID: ICON_ID,
        guidItem: windows_sys::core::GUID::default(),
    };
    let mut rect: RECT = zeroed();
    let hr = Shell_NotifyIconGetRect(&identifier, &mut rect);
    if hr == 0 && rect.right > rect.left && rect.bottom > rect.top {
        Some(rect)
    } else {
        None
    }
}

fn hover_anchor(wparam: WPARAM) -> (i32, i32) {
    let x = (wparam & 0xffff) as u16 as i16 as i32;
    let y = ((wparam >> 16) & 0xffff) as u16 as i16 as i32;
    if x != 0 || y != 0 {
        return (x, y);
    }

    unsafe {
        let mut point = POINT { x: 0, y: 0 };
        if GetCursorPos(&mut point) == 0 {
            (0, 0)
        } else {
            (point.x, point.y)
        }
    }
}

unsafe fn create_usage_icon(windows: &[UsageWindow], plans: &[ProviderPlan]) -> Result<HICON> {
    let bars = IconLayout::compute(windows, plans);
    render_usage_icon(&bars)
}

#[derive(Clone, Copy)]
struct TrayIconBar {
    y: i32,
    height: i32,
    used_percent: Option<f64>,
}

unsafe fn render_usage_icon(ordered: &[IconBar]) -> Result<HICON> {
    let bars = assign_tray_icon_bars(ordered);
    let mut xor = vec![0_u8; TRAY_ICON_SIZE * TRAY_ICON_SIZE * 4];
    let and = [0_u8; TRAY_ICON_SIZE * TRAY_ICON_SIZE / 8];

    for y in TRAY_ICON_PLATE_INSET..(TRAY_ICON_SIZE_I32 - TRAY_ICON_PLATE_INSET) {
        for x in TRAY_ICON_PLATE_INSET..(TRAY_ICON_SIZE_I32 - TRAY_ICON_PLATE_INSET) {
            put_bgra_pixel(&mut xor, x, y, (60, 60, 70));
        }
    }

    for bar in bars {
        for y in bar.y..(bar.y + bar.height) {
            for x in TRAY_ICON_BAR_LEFT..TRAY_ICON_BAR_RIGHT {
                put_bgra_pixel(&mut xor, x, y, (80, 80, 90));
            }
        }

        let Some(percent) = bar.used_percent else {
            continue;
        };
        let color = tray_usage_color(percent);
        let clamped = percent.clamp(0.0, 100.0);
        let bar_width = TRAY_ICON_BAR_RIGHT - TRAY_ICON_BAR_LEFT;
        let fill_end = (TRAY_ICON_BAR_LEFT
            + (f64::from(bar_width) * clamped / 100.0).round() as i32)
            .min(TRAY_ICON_BAR_RIGHT);
        for y in bar.y..(bar.y + bar.height) {
            for x in TRAY_ICON_BAR_LEFT..fill_end {
                put_bgra_pixel(&mut xor, x, y, color);
            }
        }
    }

    let icon = CreateIcon(
        null_mut(),
        TRAY_ICON_SIZE_I32,
        TRAY_ICON_SIZE_I32,
        1,
        32,
        and.as_ptr(),
        xor.as_ptr(),
    );
    if icon.is_null() {
        Err(anyhow!("failed to create dynamic tray icon"))
    } else {
        Ok(icon)
    }
}

fn assign_tray_icon_bars(ordered: &[IconBar]) -> Vec<TrayIconBar> {
    let count = ordered.len();
    let mut bars = Vec::with_capacity(count);
    if count == 0 {
        return bars;
    }

    let total_separator: i32 = ordered
        .windows(2)
        .map(|pair| {
            if pair[0].provider == pair[1].provider {
                TRAY_ICON_SEP_SAME_PROVIDER
            } else {
                TRAY_ICON_SEP_CROSS_PROVIDER
            }
        })
        .sum();
    let available = TRAY_ICON_CONTENT_BOTTOM - TRAY_ICON_CONTENT_TOP - total_separator;
    let ratios = tray_icon_height_ratios(count);
    let total_ratio: f64 = ratios.iter().sum();

    let mut y = TRAY_ICON_CONTENT_TOP;
    for (index, bar) in ordered.iter().enumerate() {
        let height = if index == count - 1 {
            TRAY_ICON_CONTENT_BOTTOM - y
        } else {
            (f64::from(available) * ratios[index] / total_ratio).round() as i32
        };
        bars.push(TrayIconBar {
            y,
            height,
            used_percent: bar.used_percent,
        });
        y += height;
        if let Some(next) = ordered.get(index + 1) {
            y += if bar.provider == next.provider {
                TRAY_ICON_SEP_SAME_PROVIDER
            } else {
                TRAY_ICON_SEP_CROSS_PROVIDER
            };
        }
    }

    bars
}

fn tray_icon_height_ratios(count: usize) -> Vec<f64> {
    match count {
        1 => vec![1.0],
        2 => vec![1.0, 1.0],
        3 => vec![2.0, 1.0, 1.0],
        4 => vec![1.0, 1.0, 1.0, 1.0],
        _ => vec![1.0; count],
    }
}

fn tray_usage_color(percent: f64) -> (u8, u8, u8) {
    if percent < 50.0 {
        (76, 175, 80)
    } else if percent < 80.0 {
        (255, 193, 7)
    } else if percent < 95.0 {
        (255, 152, 0)
    } else {
        (244, 67, 54)
    }
}

fn put_bgra_pixel(buffer: &mut [u8], x: i32, y: i32, (r, g, b): (u8, u8, u8)) {
    if x < 0 || y < 0 || x >= TRAY_ICON_SIZE_I32 || y >= TRAY_ICON_SIZE_I32 {
        return;
    }
    let index = ((y as usize * TRAY_ICON_SIZE) + x as usize) * 4;
    buffer[index] = b;
    buffer[index + 1] = g;
    buffer[index + 2] = r;
    buffer[index + 3] = 255;
}

fn ensure_startup_registered() -> Result<()> {
    let exe = std::env::current_exe()?;
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    let (key, _) = hkcu.create_subkey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")?;
    key.set_value("UsageBar", &format!("\"{}\"", exe.display()))?;
    Ok(())
}

fn build_tooltip_html() -> String {
    let css = include_str!("../../../assets/usagebar.css");
    let js = include_str!("../../../assets/tooltip.js");
    let override_css = r#"
        html,body{margin:0;padding:0;overflow:hidden;background:var(--app-bg);}
        #root{width:100%;}
        .menu-surface--tray{box-shadow:none;}
        .menu-surface--tray .menu-stack__item:has(.menu-card--balance){padding:1px 0;}
        .menu-surface--tray .menu-card--balance{padding-top:3px;padding-bottom:3px;gap:0;}
        .menu-surface--tray .menu-card--balance .menu-card__name{font-size:12px;font-weight:500;}
        .menu-surface--tray .menu-card--balance .menu-card__email{font-size:12px;}
        .menu-surface--tray .menu-card__metrics{display:flex;flex-direction:column;gap:10px;}
        .menu-surface--tray .menu-card__plan-inline{margin-left:auto;font-size:11px;font-weight:500;color:var(--text-secondary);white-space:nowrap;}
    "#;

    format!(
        r#"<!doctype html><html data-theme="dark"><head><meta charset="utf-8">
<style>{css}</style><style>{override_css}</style></head>
<body><div id="root"></div><script>{js}</script></body></html>"#
    )
}

fn tooltip_webview_bounds(height_css: i32) -> WebViewRect {
    WebViewRect {
        position: dpi::LogicalPosition::new(0.0, 0.0).into(),
        size: dpi::LogicalSize::new(TOOLTIP_WIDTH as f64, height_css as f64).into(),
    }
}

fn scaled(value: i32, scale: f64) -> i32 {
    (f64::from(value) * scale).round() as i32
}

unsafe fn window_scale(hwnd: HWND) -> f64 {
    let dpi = GetDpiForWindow(hwnd);
    if dpi == 0 { 1.0 } else { f64::from(dpi) / 96.0 }
}

unsafe fn work_area() -> RECT {
    let mut rect = RECT {
        left: 0,
        top: 0,
        right: 1920,
        bottom: 1040,
    };
    let ok = SystemParametersInfoW(SPI_GETWORKAREA, 0, &mut rect as *mut RECT as *mut c_void, 0);
    if ok == 0 || rect.right <= rect.left || rect.bottom <= rect.top {
        RECT {
            left: 0,
            top: 0,
            right: 1920,
            bottom: 1040,
        }
    } else {
        rect
    }
}

fn copy_wide<const N: usize>(target: &mut [u16; N], value: &str) {
    let text = wide(&shorten(value, N.saturating_sub(1)));
    for (slot, ch) in target.iter_mut().zip(text) {
        *slot = ch;
    }
}

fn wide(value: &str) -> Vec<u16> {
    value.encode_utf16().chain(Some(0)).collect()
}

fn shorten(value: &str, max_chars: usize) -> String {
    value.chars().take(max_chars).collect()
}
