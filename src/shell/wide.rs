//! Small Win32 string + window-class helpers shared by the tray and tooltip
//! windows. Keeps the UTF-16 marshalling and class-registration boilerplate in
//! one place instead of duplicating it across [`super::tray`] and
//! [`super::webview_tooltip`].

use crate::shell::native::{
    GetModuleHandleW, Hicon, Hinstance, Hwnd, RegisterClassExW, WndClassExW,
};

/// Encodes `s` as a NUL-terminated UTF-16 string for Win32 `*W` APIs.
pub fn wide_nul(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

/// Encodes `s` into a fixed `[u16; N]` buffer, NUL-terminated and truncated to
/// `N - 1` code units (e.g. `NOTIFYICONDATA.szTip` / `szInfo`). Truncation
/// happens on UTF-16 code-unit boundaries, so — unlike byte slicing — it never
/// panics on multi-byte input.
pub fn to_wide_array<const N: usize>(s: &str) -> [u16; N] {
    let mut buf = [0u16; N];
    let cap = N.saturating_sub(1);
    let wide: Vec<u16> = s.encode_utf16().take(cap).collect();
    buf[..wide.len()].copy_from_slice(&wide);
    buf
}

/// Win32 window-procedure signature shared by every window class we register.
pub type WndProc = unsafe extern "system" fn(Hwnd, u32, usize, isize) -> isize;

/// Registers a unique window class for `wndproc` and returns the class-name
/// buffer (which the caller must keep alive until the window is created) and
/// the module instance handle.
///
/// The class name is `"{prefix}-{uuid}"` so repeated registrations within the
/// same process never collide.
pub fn register_window_class(
    wndproc: WndProc,
    prefix: &str,
) -> anyhow::Result<(Vec<u16>, Hinstance)> {
    let instance = unsafe { GetModuleHandleW(std::ptr::null()) };
    if instance.0 == 0 {
        anyhow::bail!("Failed to get module handle.");
    }

    let class_name = wide_nul(&format!(
        "{prefix}-{}",
        uuid::Uuid::new_v4().to_string().replace('-', "")
    ));

    let wc = WndClassExW {
        cbSize: std::mem::size_of::<WndClassExW>() as u32,
        style: 0,
        lpfnWndProc: Some(wndproc),
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
    if unsafe { RegisterClassExW(&wc) } == 0 {
        anyhow::bail!(
            "Failed to register window class. Error: {}",
            std::io::Error::last_os_error()
        );
    }

    Ok((class_name, instance))
}
