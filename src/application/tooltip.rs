use crate::domain::{window_display_label, TooltipCard, TooltipMetric, UsageBarWindow, UsageBlock};

/// Formats a list of `UsageBlock` values into a multi-line tooltip string,
/// truncating to 127 characters (the Win32 `NOTIFYICONDATA.szTip` limit).
pub fn format(blocks: &[UsageBlock]) -> String {
    if blocks.is_empty() {
        return "UsageBarRust\nNo configured providers".to_string();
    }

    let lines: Vec<String> = blocks
        .iter()
        .flat_map(|block| {
            if block.inline {
                vec![format!("{} {}", block.label, block.value)]
            } else {
                vec![block.label.clone(), block.value.clone()]
            }
        })
        .collect();

    let text = lines.join("\n");
    if text.len() <= 127 {
        text
    } else {
        text[..127].to_string()
    }
}

/// Builds the provider cards for the custom (non-legacy) tooltip window.
///
/// Rate-limit windows are grouped by provider into a [`TooltipCard`] with one
/// [`TooltipMetric`] per window; the matching usage block (matched by provider
/// name + window label) supplies the reset hint. Any block without a matching
/// window (balance/credit providers) becomes its own card with a plain value
/// line. Unlike [`format`], the result is not truncated.
pub fn build_cards(
    blocks: &[UsageBlock],
    windows: &[UsageBarWindow],
    plans: &[(String, String)],
) -> Vec<TooltipCard> {
    let mut cards: Vec<TooltipCard> = Vec::new();
    let mut consumed = vec![false; blocks.len()];

    let plan_for = |name: &str| -> Option<String> {
        plans
            .iter()
            .find(|(provider, _)| provider == name)
            .map(|(_, plan)| plan.clone())
    };

    for window in windows {
        let mut detail = String::new();
        for (index, block) in blocks.iter().enumerate() {
            if !consumed[index]
                && block.label.contains(&window.provider_name)
                && block.label.contains(&window.window_label)
            {
                consumed[index] = true;
                detail = reset_hint(&block.value);
                break;
            }
        }

        let metric = TooltipMetric {
            label: window_display_label(&window.window_label).to_string(),
            percent: window.used_percent.clamp(0.0, 100.0),
            detail,
        };

        match cards.iter_mut().find(|c| c.title == window.provider_name) {
            Some(card) => card.metrics.push(metric),
            None => cards.push(TooltipCard {
                plan: plan_for(&window.provider_name),
                title: window.provider_name.clone(),
                metrics: vec![metric],
                lines: Vec::new(),
            }),
        }
    }

    for (index, block) in blocks.iter().enumerate() {
        if consumed[index] {
            continue;
        }
        let title = block.label.trim_end_matches(':').trim().to_string();
        cards.push(TooltipCard {
            plan: plan_for(&title),
            title,
            metrics: Vec::new(),
            lines: vec![block.value.clone()],
        });
    }

    if cards.is_empty() {
        cards.push(TooltipCard {
            title: "UsageBarRust".to_string(),
            plan: None,
            metrics: Vec::new(),
            lines: vec!["No configured providers".to_string()],
        });
    }

    cards.sort_by_key(card_order);
    cards
}

fn card_order(card: &TooltipCard) -> (u8, u8) {
    let kind = if card.metrics.is_empty() { 1 } else { 0 };
    let provider = match card.title.as_str() {
        "Codex" => 0,
        "Claude" => 1,
        _ => 2,
    };
    (kind, provider)
}

/// Extracts the reset hint from a usage value string such as
/// `"53%, 2h 10m"` → `"2h 10m"`. Returns an empty string when the value has
/// no comma-separated hint (e.g. a balance like `"$12.34"`).
fn reset_hint(value: &str) -> String {
    match value.find(',') {
        Some(index) => value[index + 1..].trim().to_string(),
        None => String::new(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn block(label: &str, value: &str) -> UsageBlock {
        UsageBlock {
            label: label.to_string(),
            value: value.to_string(),
            inline: true,
        }
    }

    fn window(provider: &str, label: &str, percent: f64) -> UsageBarWindow {
        UsageBarWindow {
            provider_name: provider.to_string(),
            window_label: label.to_string(),
            used_percent: percent,
        }
    }

    #[test]
    fn build_cards_pairs_windows_with_their_block_for_the_reset_hint() {
        let blocks = vec![block("Codex 5h:", "53%, 2h 10m")];
        let windows = vec![window("Codex", "5h", 53.0)];

        let cards = build_cards(&blocks, &windows, &[]);

        assert_eq!(cards.len(), 1);
        assert_eq!(cards[0].title, "Codex");
        assert_eq!(cards[0].metrics.len(), 1);
        let metric = &cards[0].metrics[0];
        assert_eq!(metric.label, "Session");
        assert_eq!(metric.detail, "2h 10m");
        assert_eq!(metric.percent, 53.0);
    }

    #[test]
    fn build_cards_groups_multiple_windows_under_one_provider() {
        let blocks = vec![
            block("Codex 5h:", "20%, 1h"),
            block("Codex 7d:", "40%, 3d 2h"),
        ];
        let windows = vec![window("Codex", "5h", 20.0), window("Codex", "7d", 40.0)];

        let cards = build_cards(&blocks, &windows, &[]);

        assert_eq!(cards.len(), 1);
        assert_eq!(cards[0].title, "Codex");
        assert_eq!(cards[0].metrics.len(), 2);
        assert_eq!(cards[0].metrics[0].label, "Session");
        assert_eq!(cards[0].metrics[1].label, "Weekly");
    }

    #[test]
    fn build_cards_renders_balance_blocks_as_value_lines() {
        let blocks = vec![block("DeepSeek:", "$12.34")];
        let windows = vec![];

        let cards = build_cards(&blocks, &windows, &[]);

        assert_eq!(cards.len(), 1);
        assert_eq!(cards[0].title, "DeepSeek");
        assert!(cards[0].metrics.is_empty());
        assert_eq!(cards[0].lines, vec!["$12.34".to_string()]);
    }

    #[test]
    fn build_cards_keeps_metric_cards_first_then_balance_cards() {
        let blocks = vec![
            block("Claude 5h:", "20%, 1h"),
            block("OpenRouter:", "$5.00"),
        ];
        let windows = vec![window("Claude", "5h", 20.0)];

        let cards = build_cards(&blocks, &windows, &[]);

        assert_eq!(cards.len(), 2);
        assert_eq!(cards[0].title, "Claude");
        assert!(!cards[0].metrics.is_empty());
        assert_eq!(cards[1].title, "OpenRouter");
        assert!(cards[1].metrics.is_empty());
    }

    #[test]
    fn build_cards_orders_codex_before_claude() {
        let blocks = vec![
            block("Claude 5h:", "20%, 1h"),
            block("Codex 5h:", "30%, 2h"),
        ];
        let windows = vec![window("Claude", "5h", 20.0), window("Codex", "5h", 30.0)];

        let cards = build_cards(&blocks, &windows, &[]);

        assert_eq!(cards.len(), 2);
        assert_eq!(cards[0].title, "Codex");
        assert_eq!(cards[1].title, "Claude");
    }

    #[test]
    fn build_cards_clamps_percentages() {
        let windows = vec![window("Codex", "5h", 142.0)];
        let cards = build_cards(&[], &windows, &[]);
        assert_eq!(cards[0].metrics[0].percent, 100.0);
    }

    #[test]
    fn build_cards_falls_back_when_empty() {
        let cards = build_cards(&[], &[], &[]);
        assert_eq!(cards.len(), 1);
        assert!(cards[0].metrics.is_empty());
        assert!(!cards[0].lines.is_empty());
    }

    #[test]
    fn reset_hint_extracts_text_after_comma() {
        assert_eq!(reset_hint("53%, 2h 10m"), "2h 10m");
        assert_eq!(reset_hint("$12.34"), "");
        assert_eq!(reset_hint("80%, now"), "now");
    }
}
