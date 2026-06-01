#[cfg(windows)]
fn main() {
    let mut res = winres::WindowsResource::new();
    res.set_icon("assets/AppIcon.ico");
    // Windows derives the toast/balloon notification's app name from the exe's
    // version-info resource (FileDescription/ProductName), falling back to the
    // exe filename. Set these so the notification shows "Usage Bar".
    res.set("FileDescription", "Usage Bar");
    res.set("ProductName", "Usage Bar");
    res.compile().unwrap();
}

#[cfg(not(windows))]
fn main() {}
