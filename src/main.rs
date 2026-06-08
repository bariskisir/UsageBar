#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

#[cfg(windows)]
#[tokio::main]
async fn main() -> anyhow::Result<()> {
    usagebar::platform::windows::run_usagebar()
}

#[cfg(not(windows))]
fn main() {
    eprintln!("UsageBar is a Windows notification-area application.");
}
