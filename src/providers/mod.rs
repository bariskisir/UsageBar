pub mod claude;
pub mod codex;
pub mod deepgram;
pub mod deepseek;
pub mod json_helpers;
pub mod openrouter;

/// Capitalises the first character of `s`, leaving the rest unchanged.
/// Returns an empty string for empty input. Used to label plan/tier ids that
/// have no dedicated display name.
pub(crate) fn capitalize_first(s: &str) -> String {
    let mut chars = s.chars();
    match chars.next() {
        None => String::new(),
        Some(first) => first.to_uppercase().collect::<String>() + chars.as_str(),
    }
}
