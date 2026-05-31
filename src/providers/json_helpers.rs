use rust_decimal::Decimal;
use serde_json::Value;

/// Tries to get a named property from a JSON object.
/// Returns `None` when `element` is not an Object or the property is absent.
pub fn try_get_property<'a>(element: &'a Value, property_name: &str) -> Option<&'a Value> {
    element.as_object()?.get(property_name)
}

/// Reads a named property as `Decimal`, from either a number or a string value.
pub fn get_decimal(element: &Value, property_name: &str) -> Option<Decimal> {
    let property = try_get_property(element, property_name)?;
    parse_decimal(property)
}

/// Parses a `Value` as `Decimal` (number or string).
pub fn parse_decimal(value: &Value) -> Option<Decimal> {
    match value {
        Value::Number(n) => {
            if let Some(d) = n.as_f64() {
                Decimal::from_f64_retain(d)
            } else {
                None
            }
        }
        Value::String(s) => s.parse::<Decimal>().ok(),
        _ => None,
    }
}

/// Reads a named property as `f64`, from either a number or a string value.
pub fn get_double(element: &Value, property_name: &str) -> Option<f64> {
    let property = try_get_property(element, property_name)?;
    match property {
        Value::Number(n) => n.as_f64(),
        Value::String(s) => s.parse::<f64>().ok(),
        _ => None,
    }
}

/// Returns the first matching string property from the object.
pub fn get_string(element: &Value, property_names: &[&str]) -> Option<String> {
    for name in property_names {
        if let Some(property) = try_get_property(element, name) {
            if let Some(s) = property.as_str() {
                return Some(s.to_string());
            }
        }
    }
    None
}
