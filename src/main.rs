// Windows subsystem prevents a console window from appearing.
#![windows_subsystem = "windows"]

mod application;
mod domain;
mod infrastructure;
mod providers;
mod shell;

use application::host::UsageBarRustHost;
use infrastructure::logger::FatalErrorLogger;

fn main() {
    // Declare the process per-monitor DPI aware (before any window is created)
    // so the WebView2 tooltip renders text at native resolution instead of
    // being bitmap-scaled (which looks blurry on HiDPI displays).
    unsafe {
        let _ = shell::native::SetProcessDpiAwarenessContext(
            shell::native::DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2,
        );
    }

    // Build a multi-threaded Tokio runtime so the refresh coordinator can
    // run on worker threads while the main thread services the Win32 message
    // pump (matching the C# STA thread + thread-pool model).
    let rt = match tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
    {
        Ok(rt) => rt,
        Err(err) => {
            FatalErrorLogger::log(&err.into());
            return;
        }
    };

    // The Tokio runtime must stay alive for the whole process lifetime.
    let _guard = rt.enter();

    match UsageBarRustHost::create_default() {
        Ok(host) => {
            if let Err(err) = host.run() {
                FatalErrorLogger::log(&err);
            }
        }
        Err(err) => {
            FatalErrorLogger::log(&err);
        }
    }

    // Let remaining tasks drain briefly before the runtime drops.
    rt.shutdown_timeout(std::time::Duration::from_secs(2));
}
