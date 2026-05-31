use crate::domain::UsageBarWindow;
use crate::shell::native::{CreateIcon, Hicon, Hinstance};

const ICON_SIZE: i32 = 32;
const BORDER_WIDTH: i32 = 2;
const CONTENT_WIDTH: i32 = ICON_SIZE - (BORDER_WIDTH * 2);
const CONTENT_TOP: i32 = BORDER_WIDTH;
const CONTENT_BOTTOM: i32 = ICON_SIZE - BORDER_WIDTH;
const CONTENT_HEIGHT: i32 = CONTENT_BOTTOM - CONTENT_TOP;

// Separator widths in pixels.
const SEP_SAME_PROVIDER: i32 = 1;
const SEP_CROSS_PROVIDER: i32 = 2;

/// Creates a 32×32 tray icon from provider usage windows.
/// Layout is determined by which windows are present (see `BuildBarLayout` logic).
pub fn create_usage_icon(windows: &[UsageBarWindow]) -> anyhow::Result<Hicon> {
    let bars = build_bar_layout(windows);
    render_icon(&bars)
}

// ---------------------------------------------------------------------------
// Bar layout
// ---------------------------------------------------------------------------

struct BarSpec {
    y: i32,
    height: i32,
    used_percent: Option<f64>,
}

/// Determines the ordered bar layout based on which Codex / Claude windows exist.
/// Mirrors the C# `BuildBarLayout` logic.
fn build_bar_layout(windows: &[UsageBarWindow]) -> Vec<BarSpec> {
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
    let codex_is_free = codex_5h.is_none() && codex_7d.is_some();
    let codex_is_pro = codex_5h.is_some();
    let claude_is_subscriber = claude_5h.is_some() && claude_7d.is_some();

    // Build ordered list with provider tags for separator logic.
    let mut ordered: Vec<(Option<f64>, &str)> = Vec::new();

    if codex_is_pro && has_claude && claude_is_subscriber {
        // Case 5: Codex 5h+7d + Claude 5h+7d → 25-25-25-25
        ordered.push((codex_5h.unwrap().used_percent.into(), "Codex"));
        ordered.push((codex_7d.unwrap().used_percent.into(), "Codex"));
        ordered.push((claude_5h.unwrap().used_percent.into(), "Claude"));
        ordered.push((claude_7d.unwrap().used_percent.into(), "Claude"));
    } else if codex_is_free && has_claude && claude_is_subscriber {
        // Case 4: Codex free (7d only) + Claude 5h+7d → 50-25-25
        ordered.push((codex_7d.unwrap().used_percent.into(), "Codex"));
        ordered.push((claude_5h.unwrap().used_percent.into(), "Claude"));
        ordered.push((claude_7d.unwrap().used_percent.into(), "Claude"));
    } else if !has_codex && claude_is_subscriber {
        // Case 3: Claude only 5h+7d → 50-50
        ordered.push((claude_5h.unwrap().used_percent.into(), "Claude"));
        ordered.push((claude_7d.unwrap().used_percent.into(), "Claude"));
    } else if codex_is_pro && !has_claude {
        // Case 2: Codex pro 5h+7d only → 50-50
        ordered.push((codex_5h.unwrap().used_percent.into(), "Codex"));
        if let Some(w) = codex_7d {
            ordered.push((w.used_percent.into(), "Codex"));
        }
    } else if codex_is_free && !has_claude {
        // Case 1: Codex free (7d only) → full bar
        ordered.push((codex_7d.unwrap().used_percent.into(), "Codex"));
    } else if has_codex && has_claude {
        // Fallback mixed: show whatever we have in a reasonable order.
        if let Some(w) = codex_5h {
            ordered.push((w.used_percent.into(), "Codex"));
        }
        if let Some(w) = codex_7d {
            ordered.push((w.used_percent.into(), "Codex"));
        }
        if let Some(w) = claude_5h {
            ordered.push((w.used_percent.into(), "Claude"));
        }
        if let Some(w) = claude_7d {
            ordered.push((w.used_percent.into(), "Claude"));
        }
    } else if has_claude {
        // Claude only, not subscriber (single window or mismatched).
        if let Some(w) = claude_5h {
            ordered.push((w.used_percent.into(), "Claude"));
        }
        if let Some(w) = claude_7d {
            ordered.push((w.used_percent.into(), "Claude"));
        }
    }

    // Fallback: empty gray bar.
    if ordered.is_empty() {
        ordered.push((None, "None"));
    }

    assign_bar_positions(&ordered)
}

fn find_window<'a>(windows: &[&'a UsageBarWindow], label: &str) -> Option<&'a UsageBarWindow> {
    windows.iter().find(|w| w.window_label == label).copied()
}

/// Assign pixel positions to ordered bars. Separators are 2 px between
/// different providers and 1 px within the same provider. The remaining
/// space is divided according to the case-dependent ratio.
fn assign_bar_positions(ordered: &[(Option<f64>, &str)]) -> Vec<BarSpec> {
    let n = ordered.len();
    if n == 0 {
        return Vec::new();
    }

    // Calculate total separator height.
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
            // Last bar fills to the bottom.
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

        // Add separator after this bar (except after the last).
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

/// Return the relative height ratios for each bar position.
fn get_height_ratios(n: usize) -> Vec<f64> {
    match n {
        1 => vec![1.0],
        2 => vec![1.0, 1.0],             // 50-50
        3 => vec![2.0, 1.0, 1.0],         // 50-25-25
        4 => vec![1.0, 1.0, 1.0, 1.0],    // 25-25-25-25
        _ => vec![1.0; n],                // equal split fallback
    }
}

// ---------------------------------------------------------------------------
// Rendering
// ---------------------------------------------------------------------------

fn render_icon(bars: &[BarSpec]) -> anyhow::Result<Hicon> {
    let has_any_usage = bars.iter().any(|b| b.used_percent.is_some());

    let mut xor = vec![0u8; (ICON_SIZE * ICON_SIZE * 4) as usize];
    let and = vec![0u8; (ICON_SIZE * ICON_SIZE / 8) as usize];

    for y in 0..ICON_SIZE {
        for x in 0..ICON_SIZE {
            let index = ((y * ICON_SIZE + x) * 4) as usize;

            let is_border = x < BORDER_WIDTH
                || y < BORDER_WIDTH
                || x >= ICON_SIZE - BORDER_WIDTH
                || y >= ICON_SIZE - BORDER_WIDTH;

            if is_border || is_separator_pixel(y, bars) {
                // Light gray
                xor[index] = 245;
                xor[index + 1] = 245;
                xor[index + 2] = 245;
                xor[index + 3] = 255;
                continue;
            }

            match find_bar(y, bars) {
                None => {
                    // Dark background
                    xor[index] = 32;
                    xor[index + 1] = 36;
                    xor[index + 2] = 41;
                    xor[index + 3] = 255;
                }
                Some(bar) => {
                    let (r, g, b) = get_accent(bar.used_percent);
                    let filled_width = get_filled_width(bar.used_percent, !has_any_usage);
                    let is_filled = x < filled_width;

                    if is_filled {
                        xor[index] = b;
                        xor[index + 1] = g;
                        xor[index + 2] = r;
                    } else {
                        xor[index] = 32;
                        xor[index + 1] = 36;
                        xor[index + 2] = 41;
                    }
                    xor[index + 3] = 255;
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

fn is_separator_pixel(y: i32, bars: &[BarSpec]) -> bool {
    for i in 0..bars.len().saturating_sub(1) {
        let bar_bottom = bars[i].y + bars[i].height;
        let next_bar_top = bars[i + 1].y;
        if y >= bar_bottom && y < next_bar_top {
            return true;
        }
    }
    false
}

fn find_bar(y: i32, bars: &[BarSpec]) -> Option<&BarSpec> {
    bars.iter().find(|bar| y >= bar.y && y < bar.y + bar.height)
}

fn get_filled_width(used_percent: Option<f64>, fill_when_unknown: bool) -> i32 {
    match used_percent {
        None => {
            if fill_when_unknown {
                ICON_SIZE - BORDER_WIDTH
            } else {
                BORDER_WIDTH
            }
        }
        Some(p) => {
            let clamped = p.clamp(0.0, 100.0);
            BORDER_WIDTH + (CONTENT_WIDTH as f64 * clamped / 100.0).round() as i32
        }
    }
}

// ---------------------------------------------------------------------------
// Dynamic color (green → yellow → red gradient)
//   0%   → green  (0,   255, 0)
//  50%   → yellow (255, 255, 0)
// 100%   → red    (255, 0,   0)
// 0-50:   green→yellow (R ramps up),  50-100: yellow→red (G ramps down).
// ---------------------------------------------------------------------------

fn get_accent(used_percent: Option<f64>) -> (u8, u8, u8) {
    let pct = match used_percent {
        None => return (140, 145, 152), // gray
        Some(v) => v.clamp(0.0, 100.0),
    };

    if pct <= 50.0 {
        let t = pct / 50.0;                             // 0 → 1
        ((255.0 * t).round() as u8, 255u8, 0u8)         // R: 0→255, G: 255, B: 0
    } else {
        let t = (pct - 50.0) / 50.0;                     // 0 → 1
        (255u8, (255.0 * (1.0 - t)).round() as u8, 0u8)  // R: 255, G: 255→0, B: 0
    }
}
