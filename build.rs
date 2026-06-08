#[cfg(windows)]
fn main() {
    let mut resource = winresource::WindowsResource::new();
    resource
        .set_icon("assets/AppIcon.ico")
        .set("FileDescription", "Usage Bar")
        .set("ProductName", "Usage Bar")
        .set("CompanyName", "UsageBar")
        .set("OriginalFilename", "UsageBar.exe");

    if let Err(error) = resource.compile() {
        println!("cargo:warning=failed to compile Windows resources: {error}");
    }
}

#[cfg(not(windows))]
fn main() {}
