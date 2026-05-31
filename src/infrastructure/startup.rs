use std::path::Path;
use winreg::enums::{HKEY_CURRENT_USER, KEY_READ, KEY_SET_VALUE, KEY_WRITE};
use winreg::RegKey;

const RUN_KEY_PATH: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";
const VALUE_NAME: &str = "UsageBarRust";

/// Registers (or updates) the `UsageBarRust` value under
/// `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` so the app launches at
/// logon.  Failures are logged but never propagated — startup registration is
/// best-effort.
pub fn ensure_registered(logger: &super::logger::AppLogger) {
    match try_ensure_registered() {
        Ok(()) => {}
        Err(err) => {
            tokio::runtime::Handle::try_current()
                .map(|handle| {
                    handle.block_on(logger.log(
                        "Failed to register UsageBarRust as a startup app.",
                        Some(&err),
                    ));
                })
                .unwrap_or_default();
        }
    }
}

fn try_ensure_registered() -> anyhow::Result<()> {
    let executable_path = get_executable_path()?;

    let run_key = RegKey::predef(HKEY_CURRENT_USER).open_subkey_with_flags(
        RUN_KEY_PATH,
        KEY_READ | KEY_SET_VALUE | KEY_WRITE,
    );

    match run_key {
        Ok(key) => {
            // Check if the existing value already points to our executable.
            if let Ok(current_command) = key.get_value::<String, _>(VALUE_NAME) {
                if points_to_executable(&current_command, &executable_path) {
                    return Ok(());
                }
            }

            // Update the value.
            let quoted = format!("\"{}\"", executable_path);
            key.set_value(VALUE_NAME, &quoted)?;
        }
        Err(_) => {
            // Key doesn't exist yet — create it.
            let (key, _) = RegKey::predef(HKEY_CURRENT_USER).create_subkey(RUN_KEY_PATH)?;
            let quoted = format!("\"{}\"", executable_path);
            key.set_value(VALUE_NAME, &quoted)?;
        }
    }

    Ok(())
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

fn get_executable_path() -> anyhow::Result<String> {
    if let Ok(path) = std::env::current_exe() {
        let path_str = path.to_string_lossy();
        if is_usagebar_executable(&path_str) {
            return Ok(path_str.into_owned());
        }

        let fallback = path.with_file_name("UsageBarRust.exe");
        if fallback.exists() {
            return Ok(fallback.to_string_lossy().into_owned());
        }
    }

    anyhow::bail!("Could not resolve the UsageBarRust executable path.");
}

fn is_usagebar_executable(path: &str) -> bool {
    let lower = path.to_lowercase();
    lower.ends_with(".exe") && !lower.ends_with("dotnet.exe")
}

fn points_to_executable(command: &str, executable_path: &str) -> bool {
    let Some(current_path) = extract_executable_path(command) else {
        return false;
    };

    let canonical_current = std::fs::canonicalize(Path::new(&current_path))
        .unwrap_or_else(|_| Path::new(&current_path).to_path_buf());
    let canonical_target = std::fs::canonicalize(Path::new(executable_path))
        .unwrap_or_else(|_| Path::new(executable_path).to_path_buf());

    canonical_current
        .to_string_lossy()
        .to_lowercase()
        .eq(&canonical_target.to_string_lossy().to_lowercase())
}

fn extract_executable_path(command: &str) -> Option<String> {
    let trimmed = command.trim();
    if trimmed.is_empty() {
        return None;
    }

    if trimmed.starts_with('"') {
        if let Some(end) = trimmed[1..].find('"') {
            return Some(trimmed[1..=end].to_string());
        }
        return None;
    }

    if let Some(pos) = trimmed.to_lowercase().find(".exe") {
        return Some(trimmed[..pos + 4].to_string());
    }

    Some(trimmed.to_string())
}
