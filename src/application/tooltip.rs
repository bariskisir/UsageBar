use crate::domain::UsageBlock;

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
