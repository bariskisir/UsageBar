use std::path::PathBuf;

/// Returns the application data directory: `%APPDATA%\UsageBarRust`.
pub fn app_data_directory() -> PathBuf {
    let appdata = std::env::var("APPDATA").unwrap_or_else(|_| {
        let home = std::env::var("USERPROFILE").unwrap_or_else(|_| ".".to_string());
        format!("{}\\AppData\\Roaming", home)
    });
    PathBuf::from(appdata).join("UsageBarRust")
}

/// Returns the full path to `%APPDATA%\UsageBarRust\settings.json`.
pub fn settings_file_path() -> PathBuf {
    app_data_directory().join("settings.json")
}

/// Returns the full path to `%APPDATA%\UsageBarRust\app.log`.
pub fn log_file_path() -> PathBuf {
    app_data_directory().join("app.log")
}
