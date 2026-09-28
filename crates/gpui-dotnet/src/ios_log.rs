struct IosLogger;

impl log::Log for IosLogger {
    fn enabled(&self, _: &log::Metadata<'_>) -> bool {
        true
    }

    fn log(&self, record: &log::Record<'_>) {
        eprintln!("[gpui-ios] {}: {}", record.level(), record.args());
    }

    fn flush(&self) {}
}

static LOGGER: IosLogger = IosLogger;

pub(crate) fn init() {
    if log::set_logger(&LOGGER).is_ok() {
        log::set_max_level(log::LevelFilter::Info);
    }
    std::panic::set_hook(Box::new(|info| eprintln!("[gpui-ios] panic: {info}")));
}
