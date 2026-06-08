use chrono::TimeDelta;

pub fn currency(value: f64, symbol: &str) -> String {
    format!("{symbol}{value:.2}")
}

pub fn reset_duration(duration: TimeDelta) -> String {
    if duration <= TimeDelta::zero() {
        return "now".to_string();
    }

    let total_minutes = ((duration.num_seconds() as f64) / 60.0).ceil().max(1.0) as i64;
    if total_minutes >= 24 * 60 {
        let days = total_minutes / (24 * 60);
        let hours = (total_minutes % (24 * 60)) / 60;
        return format!("{days}d {hours}h");
    }

    let hours = total_minutes / 60;
    let minutes = total_minutes % 60;
    if hours >= 1 {
        format!("{hours}h {minutes}m")
    } else {
        format!("{minutes}m")
    }
}

pub(crate) fn capitalize(value: &str) -> String {
    let mut chars = value.chars();
    match chars.next() {
        Some(first) => first.to_uppercase().collect::<String>() + chars.as_str(),
        None => String::new(),
    }
}
