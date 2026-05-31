use crate::shell::native::{CreateIcon, Hicon, Hinstance};

const ICON_SIZE: i32 = 32;
const BORDER_WIDTH: i32 = 2;
const SEPARATOR_TOP: i32 = 15;
const SEPARATOR_BOTTOM: i32 = 17;
const CONTENT_WIDTH: i32 = ICON_SIZE - (BORDER_WIDTH * 2);

/// Creates a 32×32 tray icon with two horizontal bar sections:
///   - Top half (y < 15):  Codex 5h  (primary)
///   - Bottom half (y ≥ 17): Codex 7d (secondary)
///
/// The two halves are separated by a light-grey separator line.  Each bar's
/// fill colour transitions green → yellow → orange → red independently.
/// When a percentage is `None` that bar is shown full and grey.
pub fn create_usage_icon(
    codex_primary_used_percent: Option<f64>,
    codex_secondary_used_percent: Option<f64>,
) -> anyhow::Result<Hicon> {
    let has_any_usage = codex_primary_used_percent.is_some() || codex_secondary_used_percent.is_some();

    let primary_accent = get_accent(codex_primary_used_percent);
    let secondary_accent = get_accent(codex_secondary_used_percent);
    let primary_filled_width = get_filled_width(codex_primary_used_percent, !has_any_usage);
    let secondary_filled_width = get_filled_width(codex_secondary_used_percent, !has_any_usage);

    let mut xor = vec![0u8; (ICON_SIZE * ICON_SIZE * 4) as usize];
    let and = vec![0u8; (ICON_SIZE * ICON_SIZE / 8) as usize];

    for y in 0..ICON_SIZE {
        for x in 0..ICON_SIZE {
            let index = ((y * ICON_SIZE + x) * 4) as usize;

            let border = x < BORDER_WIDTH
                || y < BORDER_WIDTH
                || x >= ICON_SIZE - BORDER_WIDTH
                || y >= ICON_SIZE - BORDER_WIDTH;

            let separator = y >= SEPARATOR_TOP && y < SEPARATOR_BOTTOM;

            let top_fill = y < SEPARATOR_TOP && x < primary_filled_width;
            let bottom_fill = y >= SEPARATOR_BOTTOM && x < secondary_filled_width;

            let (r, g, b) = if border || separator {
                (245u8, 245u8, 245u8) // light gray
            } else if top_fill {
                primary_accent
            } else if bottom_fill {
                secondary_accent
            } else {
                (32u8, 36u8, 41u8) // dark background
            };

            // BGRA byte order.
            xor[index] = b;
            xor[index + 1] = g;
            xor[index + 2] = r;
            xor[index + 3] = 255;
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

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

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

fn get_accent(used_percent: Option<f64>) -> (u8, u8, u8) {
    match used_percent {
        None => (140, 145, 152),
        Some(v) if v <= 30.0 => (35, 170, 88),
        Some(v) if v <= 70.0 => (230, 190, 58),
        Some(v) if v < 100.0 => (236, 126, 42),
        Some(_) => (218, 55, 55),
    }
}
