use std::{
    cell::Cell,
    rc::Rc,
    sync::{Arc, RwLock},
};

use chrono::{Datelike as _, NaiveDate, Weekday};
use gpui::{
    AnyElement, App, AppContext as _, Entity, IntoElement as _, SharedString, Subscription, Window,
};
use gpui_component::{
    Disableable as _, Sizable as _, Size,
    calendar::{Calendar, CalendarEvent, CalendarState, Date, Matcher},
    date_picker::{DatePicker, DatePickerEvent, DatePickerState},
};
use gpui_dotnet::extension::{
    NativeExtensionEventEmitter, NativeExtensionRequest, NativeExtensionStore,
};

use crate::component_schema::*;

const NO_DAY: u32 = u32::MAX;

#[derive(Clone, Copy, PartialEq, Eq)]
struct Constraints {
    minimum: Option<NaiveDate>,
    maximum: Option<NaiveDate>,
    weekdays: u32,
}

impl Constraints {
    fn excludes(self, date: &NaiveDate) -> bool {
        self.minimum.is_some_and(|minimum| *date < minimum)
            || self.maximum.is_some_and(|maximum| *date > maximum)
            || self.weekdays & (1 << date.weekday().num_days_from_sunday()) != 0
    }
}

#[derive(Clone, Copy)]
struct DateSnapshot {
    value: Date,
    constraints: Constraints,
    first_day: Weekday,
    months: usize,
    first_year: i32,
    last_year: i32,
}

fn decode_day(day: u32) -> Option<Option<NaiveDate>> {
    if day == NO_DAY {
        return Some(None);
    }
    let ce_day = i32::try_from(day).ok()?.checked_add(1)?;
    let date = NaiveDate::from_num_days_from_ce_opt(ce_day)?;
    (1..=9999).contains(&date.year()).then_some(Some(date))
}

fn snapshot(
    start_day: u32,
    end_day: u32,
    range: bool,
    minimum_day: u32,
    maximum_day: u32,
    disabled_weekdays: u32,
    first_day_of_week: u32,
    number_of_months: u32,
    first_year: u32,
    last_year: u32,
) -> Result<DateSnapshot, SharedString> {
    let start = decode_day(start_day).ok_or("The date start is invalid.")?;
    let end = decode_day(end_day).ok_or("The date end is invalid.")?;
    let minimum = decode_day(minimum_day).ok_or("The minimum date is invalid.")?;
    let maximum = decode_day(maximum_day).ok_or("The maximum date is invalid.")?;
    if (!range && end.is_some())
        || (end.is_some() && start.is_none())
        || start.zip(end).is_some_and(|(a, b)| a > b)
        || minimum.zip(maximum).is_some_and(|(a, b)| a > b)
        || disabled_weekdays & !0x7f != 0
        || number_of_months == 0
        || number_of_months > 2
        || first_year == 0
        || last_year > 9999
        || first_year > last_year
    {
        return Err("The date mode, range, or limits are invalid.".into());
    }
    let first_day = match first_day_of_week {
        0 => Weekday::Sun,
        1 => Weekday::Mon,
        2 => Weekday::Tue,
        3 => Weekday::Wed,
        4 => Weekday::Thu,
        5 => Weekday::Fri,
        6 => Weekday::Sat,
        _ => return Err("The first weekday is invalid.".into()),
    };
    let constraints = Constraints {
        minimum,
        maximum,
        weekdays: disabled_weekdays,
    };
    if [start, end].into_iter().flatten().any(|date| {
        date.year() < first_year as i32
            || date.year() > last_year as i32
            || constraints.excludes(&date)
    }) {
        return Err("The selected date is outside the allowed dates.".into());
    }
    Ok(DateSnapshot {
        value: if range {
            Date::Range(start, end)
        } else {
            Date::Single(start)
        },
        constraints,
        first_day,
        months: number_of_months as usize,
        first_year: first_year as i32,
        last_year: last_year as i32,
    })
}

fn encode_date(date: Date) -> [u8; 9] {
    let (range, start, end) = match date {
        Date::Single(start) => (0, start, None),
        Date::Range(start, end) => (1, start, end),
    };
    let day =
        |date: Option<NaiveDate>| date.map_or(NO_DAY, |date| (date.num_days_from_ce() - 1) as u32);
    let mut payload = [0; 9];
    payload[0] = range;
    payload[1..5].copy_from_slice(&day(start).to_le_bytes());
    payload[5..9].copy_from_slice(&day(end).to_le_bytes());
    payload
}

struct DateEvents {
    token: Cell<u64>,
    dirty: Cell<bool>,
    callback_error: Cell<Option<i32>>,
    emitter: NativeExtensionEventEmitter,
    kind: u16,
}

impl DateEvents {
    fn emit(&self, value: Date) {
        self.dirty.set(true);
        let token = self.token.get();
        if token == 0 {
            return;
        }
        if let Err(status) = self
            .emitter
            .emit(token, self.kind, 0, 0, &encode_date(value))
        {
            self.callback_error.set(Some(status));
        }
    }

    fn check_error(&self) -> Result<(), SharedString> {
        if let Some(status) = self.callback_error.take() {
            return Err(format!("The managed date callback failed with status {status}.").into());
        }
        Ok(())
    }
}

fn matcher(constraints: &Arc<RwLock<Constraints>>) -> Matcher {
    let constraints = constraints.clone();
    Matcher::custom(move |date| {
        constraints
            .read()
            .map_or(true, |current| current.excludes(date))
    })
}

fn update_constraints(
    target: &Arc<RwLock<Constraints>>,
    next: Constraints,
) -> Result<bool, SharedString> {
    let mut current = target
        .write()
        .map_err(|_| SharedString::from("Date constraints are unavailable."))?;
    if *current == next {
        return Ok(false);
    }
    *current = next;
    Ok(true)
}

struct RetainedCalendar {
    state: Entity<CalendarState>,
    accepted: Cell<Date>,
    constraints: Arc<RwLock<Constraints>>,
    mode: bool,
    years: Cell<(i32, i32)>,
    events: Rc<DateEvents>,
    _subscription: Subscription,
}

pub(super) fn calendar(
    request: NativeExtensionRequest,
    resources: &NativeExtensionStore,
    window: &mut Window,
    cx: &mut App,
) -> Result<AnyElement, SharedString> {
    if !request.children.is_empty() {
        return Err("Calendar does not accept children.".into());
    }
    let config = CalendarConfiguration::parse(&request.configuration)
        .ok_or("Invalid Calendar configuration.")?;
    let next = snapshot(
        config.start_day,
        config.end_day,
        config.range,
        config.minimum_day,
        config.maximum_day,
        config.disabled_weekdays,
        config.first_day_of_week,
        config.number_of_months,
        config.first_year,
        config.last_year,
    )?;
    let resource = resources.get_or_insert_with(&request.resource_key, || {
        let constraints = Arc::new(RwLock::new(next.constraints));
        let state = cx.new(|cx| {
            CalendarState::new(window, cx)
                .year_range((next.first_year, next.last_year + 1))
                .disabled_matcher(matcher(&constraints))
        });
        state.update(cx, |state, cx| state.set_date(next.value, window, cx));
        let events = Rc::new(DateEvents {
            token: Cell::new(config.changed_event),
            dirty: Cell::new(false),
            callback_error: Cell::new(None),
            emitter: request.events,
            kind: CALENDAR_EVENT_CHANGED,
        });
        let receiver = events.clone();
        let subscription = cx.subscribe(&state, move |_, event: &CalendarEvent, _| {
            let CalendarEvent::Selected(date) = event;
            receiver.emit(*date);
        });
        Rc::new(RetainedCalendar {
            state,
            accepted: Cell::new(next.value),
            constraints,
            mode: config.range,
            years: Cell::new((next.first_year, next.last_year)),
            events,
            _subscription: subscription,
        })
    });
    if resource.mode != config.range {
        return Err("Calendar range mode is fixed for a retained key.".into());
    }
    resource.events.token.set(config.changed_event);
    resource.events.check_error()?;
    let constraints_changed = update_constraints(&resource.constraints, next.constraints)?;
    if resource.years.get() != (next.first_year, next.last_year) {
        resource.state.update(cx, |state, cx| {
            state.set_year_range((next.first_year, next.last_year + 1), cx)
        });
        resource.years.set((next.first_year, next.last_year));
    }
    let dirty = resource.events.dirty.replace(false);
    if resource.accepted.get() != next.value || dirty || constraints_changed {
        if resource.state.read(cx).date() != next.value || constraints_changed {
            resource
                .state
                .update(cx, |state, cx| state.set_date(next.value, window, cx));
        }
    }
    resource.accepted.set(next.value);
    Ok(Calendar::new(&resource.state)
        .number_of_months(next.months)
        .first_day_of_week(next.first_day)
        .with_size(calendar_size(config.size))
        .into_any_element())
}

struct RetainedDatePicker {
    state: Entity<DatePickerState>,
    accepted: Cell<Date>,
    constraints: Arc<RwLock<Constraints>>,
    mode: bool,
    first_day: Weekday,
    years: Cell<(i32, i32)>,
    events: Rc<DateEvents>,
    _subscription: Subscription,
}

pub(super) fn date_picker(
    request: NativeExtensionRequest,
    resources: &NativeExtensionStore,
    window: &mut Window,
    cx: &mut App,
) -> Result<AnyElement, SharedString> {
    if !request.children.is_empty() {
        return Err("DatePicker does not accept children.".into());
    }
    let config = DatePickerConfiguration::parse(&request.configuration)
        .ok_or("Invalid DatePicker configuration.")?;
    let next = snapshot(
        config.start_day,
        config.end_day,
        config.range,
        config.minimum_day,
        config.maximum_day,
        config.disabled_weekdays,
        config.first_day_of_week,
        config.number_of_months,
        config.first_year,
        config.last_year,
    )?;
    let resource = resources.get_or_insert_with(&request.resource_key, || {
        let constraints = Arc::new(RwLock::new(next.constraints));
        let state = cx.new(|cx| {
            let state = if config.range {
                DatePickerState::range(window, cx)
            } else {
                DatePickerState::new(window, cx)
            };
            state
                .first_day_of_week(next.first_day)
                .disabled_matcher(matcher(&constraints))
        });
        state.update(cx, |state, cx| {
            state.set_year_range((next.first_year, next.last_year + 1), cx);
            state.set_date(next.value, window, cx);
        });
        let events = Rc::new(DateEvents {
            token: Cell::new(config.changed_event),
            dirty: Cell::new(false),
            callback_error: Cell::new(None),
            emitter: request.events,
            kind: DATE_PICKER_EVENT_CHANGED,
        });
        let receiver = events.clone();
        let subscription = cx.subscribe(&state, move |_, event: &DatePickerEvent, _| {
            let DatePickerEvent::Change(date) = event;
            receiver.emit(*date);
        });
        Rc::new(RetainedDatePicker {
            state,
            accepted: Cell::new(next.value),
            constraints,
            mode: config.range,
            first_day: next.first_day,
            years: Cell::new((next.first_year, next.last_year)),
            events,
            _subscription: subscription,
        })
    });
    if resource.mode != config.range || resource.first_day != next.first_day {
        return Err("DatePicker range mode and first weekday are fixed for a retained key.".into());
    }
    resource.events.token.set(config.changed_event);
    resource.events.check_error()?;
    let constraints_changed = update_constraints(&resource.constraints, next.constraints)?;
    if resource.years.get() != (next.first_year, next.last_year) {
        resource.state.update(cx, |state, cx| {
            state.set_year_range((next.first_year, next.last_year + 1), cx)
        });
        resource.years.set((next.first_year, next.last_year));
    }
    let dirty = resource.events.dirty.replace(false);
    if resource.accepted.get() != next.value || dirty || constraints_changed {
        if resource.state.read(cx).date() != next.value || constraints_changed {
            resource
                .state
                .update(cx, |state, cx| state.set_date(next.value, window, cx));
        }
    }
    resource.accepted.set(next.value);
    Ok(DatePicker::new(&resource.state)
        .number_of_months(next.months)
        .placeholder(config.placeholder)
        .cleanable(config.cleanable)
        .disabled(config.disabled)
        .with_size(date_picker_size(config.size))
        .into_any_element())
}

fn calendar_size(size: CalendarSize) -> Size {
    match size {
        CalendarSize::Xsmall => Size::XSmall,
        CalendarSize::Small => Size::Small,
        CalendarSize::Medium => Size::Medium,
        CalendarSize::Large => Size::Large,
    }
}

fn date_picker_size(size: DatePickerSize) -> Size {
    match size {
        DatePickerSize::Xsmall => Size::XSmall,
        DatePickerSize::Small => Size::Small,
        DatePickerSize::Medium => Size::Medium,
        DatePickerSize::Large => Size::Large,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn day_wire_round_trips_single_range_and_empty() {
        let day = NaiveDate::from_ymd_opt(2026, 9, 26).unwrap();
        let encoded = encode_date(Date::Single(Some(day)));
        assert_eq!(encoded[0], 0);
        assert_eq!(
            decode_day(u32::from_le_bytes(encoded[1..5].try_into().unwrap())),
            Some(Some(day))
        );
        assert_eq!(
            decode_day(u32::from_le_bytes(encoded[5..9].try_into().unwrap())),
            Some(None)
        );
        assert_eq!(encode_date(Date::Range(None, None))[0], 1);
    }

    #[test]
    fn date_options_reject_invalid_ranges_and_constraints() {
        let day = (NaiveDate::from_ymd_opt(2026, 9, 26)
            .unwrap()
            .num_days_from_ce()
            - 1) as u32;
        assert!(snapshot(day, NO_DAY, false, NO_DAY, NO_DAY, 0, 0, 1, 1900, 2100).is_ok());
        assert!(snapshot(day, day, false, NO_DAY, NO_DAY, 0, 0, 1, 1900, 2100).is_err());
        assert!(snapshot(day, NO_DAY, false, NO_DAY, NO_DAY, 1 << 6, 0, 1, 1900, 2100).is_err());
        assert!(snapshot(day, NO_DAY, false, NO_DAY, NO_DAY, 0, 7, 1, 1900, 2100).is_err());
    }

    #[test]
    fn matcher_reads_updated_native_constraints() {
        let date = NaiveDate::from_ymd_opt(2026, 9, 26).unwrap();
        let initial = Constraints {
            minimum: None,
            maximum: None,
            weekdays: 0,
        };
        let shared = Arc::new(RwLock::new(initial));
        let matcher = matcher(&shared);
        assert!(!matcher.matched(&date));
        assert!(!update_constraints(&shared, initial).unwrap());
        assert!(
            update_constraints(
                &shared,
                Constraints {
                    weekdays: 1 << 6,
                    ..initial
                }
            )
            .unwrap()
        );
        assert!(matcher.matched(&date));
    }
}
