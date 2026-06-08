use serde_json::Value;

pub(crate) fn get_string(value: &Value, names: &[&str]) -> Option<String> {
    names
        .iter()
        .find_map(|name| value.get(*name).and_then(Value::as_str))
        .map(ToString::to_string)
}

pub(crate) fn get_f64(value: &Value, name: &str) -> Option<f64> {
    match value.get(name)? {
        Value::Number(number) => number.as_f64(),
        Value::String(text) => text.parse::<f64>().ok(),
        _ => None,
    }
}

pub(crate) fn get_decimal(value: &Value, name: &str) -> Option<f64> {
    get_f64(value, name)
}

pub(crate) fn enumerate_array_or_property<'a>(root: &'a Value, property: &str) -> Vec<&'a Value> {
    if let Some(items) = root.as_array() {
        return items.iter().collect();
    }

    root.get(property)
        .and_then(Value::as_array)
        .map(|items| items.iter().collect())
        .unwrap_or_default()
}

pub(crate) fn enumerate_array_or_property_or_root<'a>(
    root: &'a Value,
    property: &str,
) -> Vec<&'a Value> {
    let items = enumerate_array_or_property(root, property);
    if items.is_empty() { vec![root] } else { items }
}

pub(crate) fn url_encode_path_segment(value: &str) -> String {
    value
        .bytes()
        .flat_map(|byte| match byte {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => {
                vec![byte as char]
            }
            _ => format!("%{byte:02X}").chars().collect(),
        })
        .collect()
}
