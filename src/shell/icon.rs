//! Tray icon renderer.
//!
//! The **bar-division layout** (how many bars and their proportions, based on
//! which Codex / Claude windows are present: full · 50-50 · 50-25-25 ·
//! 25-25-25-25) is UsageBarRust's own logic. The **visual style and colour
//! theme** are taken from Win-CodexBar (`rust/src/tray`): a dark plate, a grey
//! bar track, and discrete usage-level fill colours
//! (green → yellow → orange → red).

use crate::domain::UsageBarWindow;
use crate::shell::native::{CreateIcon, Hicon, Hinstance};

const ICON_SIZE: i32 = 32;

// CodexBar-style geometry: a 2px transparent margin around a dark plate, with
// the bars inset inside it.
const PLATE_INSET: i32 = 2;
const BAR_LEFT: i32 = 4;
const BAR_RIGHT: i32 = ICON_SIZE - 4; // 28
const BAR_WIDTH: i32 = BAR_RIGHT - BAR_LEFT; // 24
const CONTENT_TOP: i32 = 6;
const CONTENT_BOTTOM: i32 = ICON_SIZE - 6; // 26
const CONTENT_HEIGHT: i32 = CONTENT_BOTTOM - CONTENT_TOP; // 20

// Separator (gap) heights between stacked bars.
const SEP_SAME_PROVIDER: i32 = 1;
const SEP_CROSS_PROVIDER: i32 = 2;

// CodexBar palette.
const PLATE: (u8, u8, u8) = (60, 60, 70);
const TRACK: (u8, u8, u8) = (80, 80, 90);

/// Usage status level → fill colour. Ported from CodexBar's `UsageLevel`.
#[derive(Debug, Clone, Copy, PartialEq)]
enum UsageLevel {
    Low,
    Medium,
    High,
    Critical,
}

impl UsageLevel {
    fn from_percent(percent: f64) -> Self {
        match percent {
            p if p < 50.0 => UsageLevel::Low,
            p if p < 80.0 => UsageLevel::Medium,
            p if p < 95.0 => UsageLevel::High,
            _ => UsageLevel::Critical,
        }
    }

    fn color(&self) -> (u8, u8, u8) {
        match self {
            UsageLevel::Low => (76, 175, 80),      // Green
            UsageLevel::Medium => (255, 193, 7),   // Yellow/Amber
            UsageLevel::High => (255, 152, 0),     // Orange
            UsageLevel::Critical => (244, 67, 54), // Red
        }
    }
}

/// Creates the 32×32 tray icon from provider usage windows.
pub fn create_usage_icon(
    windows: &[UsageBarWindow],
    plans: &[(String, String)],
) -> anyhow::Result<Hicon> {
    let bars = build_bar_layout(windows, plans);
    render_icon(&bars)
}

// ---------------------------------------------------------------------------
// Bar layout (UsageBarRust's division logic)
// ---------------------------------------------------------------------------

struct BarSpec {
    y: i32,
    height: i32,
    used_percent: Option<f64>,
}

/// Determines the ordered bar layout based on which Codex / Claude windows
/// exist (mirrors the original `BuildBarLayout` cases).
fn build_bar_layout(windows: &[UsageBarWindow], plans: &[(String, String)]) -> Vec<BarSpec> {
    let codex_windows: Vec<&UsageBarWindow> = windows
        .iter()
        .filter(|w| w.provider_name == "Codex")
        .collect();
    let claude_windows: Vec<&UsageBarWindow> = windows
        .iter()
        .filter(|w| w.provider_name == "Claude")
        .collect();

    let codex_5h = find_window(&codex_windows, "5h");
    let codex_7d = find_window(&codex_windows, "7d");
    let claude_5h = find_window(&claude_windows, "5h");
    let claude_7d = find_window(&claude_windows, "7d");

    let has_codex = !codex_windows.is_empty();
    let has_claude = !claude_windows.is_empty();
    let codex_plan_is_free = provider_plan_is(plans, "Codex", "Free");
    let codex_free_window = if codex_plan_is_free {
        codex_7d.or(codex_5h)
    } else {
        codex_7d
    };
    let codex_is_free = codex_free_window.is_some() && (codex_plan_is_free || codex_5h.is_none());
    let codex_is_pro = codex_5h.is_some() && !codex_plan_is_free;
    let codex_pro_windows = if codex_is_pro {
        codex_5h.zip(codex_7d)
    } else {
        None
    };
    let claude_is_subscriber = claude_5h.is_some() && claude_7d.is_some();

    let mut ordered: Vec<(Option<f64>, &str)> = Vec::new();

    if let Some((codex_5h, codex_7d)) =
        codex_pro_windows.filter(|_| has_claude && claude_is_subscriber)
    {
        // Codex 5h+7d + Claude 5h+7d → 25-25-25-25
        ordered.push((Some(codex_5h.used_percent), "Codex"));
        ordered.push((Some(codex_7d.used_percent), "Codex"));
        ordered.push((Some(claude_5h.unwrap().used_percent), "Claude"));
        ordered.push((Some(claude_7d.unwrap().used_percent), "Claude"));
    } else if codex_is_free && has_claude && claude_is_subscriber {
        // Codex free (7d only) + Claude 5h+7d → 50-25-25
        ordered.push((Some(codex_free_window.unwrap().used_percent), "Codex"));
        ordered.push((Some(claude_5h.unwrap().used_percent), "Claude"));
        ordered.push((Some(claude_7d.unwrap().used_percent), "Claude"));
    } else if !has_codex && claude_is_subscriber {
        // Claude only 5h+7d → 50-50
        ordered.push((Some(claude_5h.unwrap().used_percent), "Claude"));
        ordered.push((Some(claude_7d.unwrap().used_percent), "Claude"));
    } else if codex_is_pro && !has_claude {
        // Codex pro 5h+7d only → 50-50
        ordered.push((Some(codex_5h.unwrap().used_percent), "Codex"));
        if let Some(w) = codex_7d {
            ordered.push((Some(w.used_percent), "Codex"));
        }
    } else if codex_is_free && !has_claude {
        // Codex free (7d only) → full bar
        ordered.push((Some(codex_free_window.unwrap().used_percent), "Codex"));
    } else if has_codex && has_claude {
        if let Some(w) = codex_5h {
            ordered.push((Some(w.used_percent), "Codex"));
        }
        if let Some(w) = codex_7d {
            ordered.push((Some(w.used_percent), "Codex"));
        }
        if let Some(w) = claude_5h {
            ordered.push((Some(w.used_percent), "Claude"));
        }
        if let Some(w) = claude_7d {
            ordered.push((Some(w.used_percent), "Claude"));
        }
    } else if has_claude {
        if let Some(w) = claude_5h {
            ordered.push((Some(w.used_percent), "Claude"));
        }
        if let Some(w) = claude_7d {
            ordered.push((Some(w.used_percent), "Claude"));
        }
    }

    if ordered.is_empty() {
        ordered.push((None, "None"));
    }

    assign_bar_positions(&ordered)
}

fn find_window<'a>(windows: &[&'a UsageBarWindow], label: &str) -> Option<&'a UsageBarWindow> {
    windows.iter().find(|w| w.window_label == label).copied()
}

fn provider_plan_is(plans: &[(String, String)], provider_name: &str, expected: &str) -> bool {
    plans
        .iter()
        .any(|(provider, plan)| provider == provider_name && plan.eq_ignore_ascii_case(expected))
}

/// Assigns pixel positions to ordered bars. Separators are 2px between
/// different providers and 1px within the same provider; the remaining space
/// is divided by the case-dependent ratio.
fn assign_bar_positions(ordered: &[(Option<f64>, &str)]) -> Vec<BarSpec> {
    let n = ordered.len();
    if n == 0 {
        return Vec::new();
    }

    let mut total_sep = 0i32;
    for i in 0..(n.saturating_sub(1)) {
        total_sep += if ordered[i].1 != ordered[i + 1].1 {
            SEP_CROSS_PROVIDER
        } else {
            SEP_SAME_PROVIDER
        };
    }

    let available = CONTENT_HEIGHT - total_sep;
    let ratios = get_height_ratios(n);
    let total_ratio: f64 = ratios.iter().sum();

    let mut bars = Vec::with_capacity(n);
    let mut y = CONTENT_TOP;

    for i in 0..n {
        let bar_height = if i == n - 1 {
            CONTENT_BOTTOM - y
        } else {
            (available as f64 * ratios[i] / total_ratio).round() as i32
        };

        bars.push(BarSpec {
            y,
            height: bar_height,
            used_percent: ordered[i].0,
        });

        y += bar_height;

        if i < n - 1 {
            y += if ordered[i].1 != ordered[i + 1].1 {
                SEP_CROSS_PROVIDER
            } else {
                SEP_SAME_PROVIDER
            };
        }
    }

    bars
}

fn get_height_ratios(n: usize) -> Vec<f64> {
    match n {
        1 => vec![1.0],
        2 => vec![1.0, 1.0],           // 50-50
        3 => vec![2.0, 1.0, 1.0],      // 50-25-25
        4 => vec![1.0, 1.0, 1.0, 1.0], // 25-25-25-25
        _ => vec![1.0; n],
    }
}

// ---------------------------------------------------------------------------
// Rendering (CodexBar visual style)
// ---------------------------------------------------------------------------

fn render_icon(bars: &[BarSpec]) -> anyhow::Result<Hicon> {
    let mut xor = vec![0u8; (ICON_SIZE * ICON_SIZE * 4) as usize]; // transparent
    let and = vec![0u8; (ICON_SIZE * ICON_SIZE / 8) as usize];

    // Dark plate (gaps between bars read as this colour).
    for y in PLATE_INSET..ICON_SIZE - PLATE_INSET {
        for x in PLATE_INSET..ICON_SIZE - PLATE_INSET {
            put_pixel(&mut xor, x, y, PLATE.0, PLATE.1, PLATE.2, 255);
        }
    }

    for bar in bars {
        // Track.
        for y in bar.y..bar.y + bar.height {
            for x in BAR_LEFT..BAR_RIGHT {
                put_pixel(&mut xor, x, y, TRACK.0, TRACK.1, TRACK.2, 255);
            }
        }
        // Fill (level colour) — only when the window has a known percentage.
        if let Some(pct) = bar.used_percent {
            let (r, g, b) = UsageLevel::from_percent(pct).color();
            let clamped = pct.clamp(0.0, 100.0);
            let fill_end =
                (BAR_LEFT + (BAR_WIDTH as f64 * clamped / 100.0).round() as i32).min(BAR_RIGHT);
            for y in bar.y..bar.y + bar.height {
                for x in BAR_LEFT..fill_end {
                    put_pixel(&mut xor, x, y, r, g, b, 255);
                }
            }
        }
    }

    let icon = unsafe {
        CreateIcon(
            Hinstance(0),
            ICON_SIZE,
            ICON_SIZE,
            1,
            32,
            and.as_ptr(),
            xor.as_ptr(),
        )
    };

    if icon.0 == 0 {
        anyhow::bail!("Failed to create tray icon.");
    }

    Ok(icon)
}

/// Writes one pixel into the BGRA XOR buffer (`CreateIcon` byte order).
#[inline]
fn put_pixel(xor: &mut [u8], x: i32, y: i32, r: u8, g: u8, b: u8, a: u8) {
    let index = ((y * ICON_SIZE + x) * 4) as usize;
    xor[index] = b;
    xor[index + 1] = g;
    xor[index + 2] = r;
    xor[index + 3] = a;
}

#[cfg(test)]
mod tests {
    use super::*;

    fn window(provider: &str, label: &str, pct: f64) -> UsageBarWindow {
        UsageBarWindow {
            provider_name: provider.to_string(),
            window_label: label.to_string(),
            used_percent: pct,
        }
    }

    #[test]
    fn level_thresholds_match_codexbar() {
        assert_eq!(UsageLevel::from_percent(10.0), UsageLevel::Low);
        assert_eq!(UsageLevel::from_percent(60.0), UsageLevel::Medium);
        assert_eq!(UsageLevel::from_percent(85.0), UsageLevel::High);
        assert_eq!(UsageLevel::from_percent(99.0), UsageLevel::Critical);
    }

    #[test]
    fn four_windows_produce_four_bars() {
        let windows = vec![
            window("Codex", "5h", 10.0),
            window("Codex", "7d", 20.0),
            window("Claude", "5h", 30.0),
            window("Claude", "7d", 40.0),
        ];
        assert_eq!(build_bar_layout(&windows, &[]).len(), 4);
    }

    #[test]
    fn claude_subscriber_produces_two_bars() {
        let windows = vec![window("Claude", "5h", 30.0), window("Claude", "7d", 40.0)];
        assert_eq!(build_bar_layout(&windows, &[]).len(), 2);
    }

    #[test]
    fn codex_free_plan_with_primary_window_and_claude_subscriber_produces_three_bars() {
        let windows = vec![
            window("Codex", "5h", 10.0),
            window("Claude", "5h", 30.0),
            window("Claude", "7d", 40.0),
        ];
        let plans = vec![("Codex".to_string(), "Free".to_string())];

        assert_eq!(build_bar_layout(&windows, &plans).len(), 3);
    }

    #[test]
    fn empty_windows_produce_single_unknown_bar() {
        let bars = build_bar_layout(&[], &[]);
        assert_eq!(bars.len(), 1);
        assert!(bars[0].used_percent.is_none());
    }

    #[test]
    fn render_icon_succeeds() {
        let bars = build_bar_layout(&[window("Codex", "5h", 50.0)], &[]);
        assert!(render_icon(&bars).is_ok());
    }
}
