import { dateKey, type Classroom } from "./domain";

export function classroomForDisplay(c: Classroom, now: number) {
  const key = dateKey(now),
    weekday = new Date(`${key}T12:00:00+08:00`).getUTCDay() || 7;
  const template = c.templates?.find(
    (t) => t.effectiveFrom <= key && key <= t.effectiveUntil,
  );
  const override = c.overrides?.[key];
  const periods =
    override?.mode === "closed"
      ? []
      : override?.mode === "custom"
        ? override.periods
        : (template?.weekdays[
            String(override?.mode === "weekday" ? override.weekday : weekday)
          ] ?? []);
  const time = new Intl.DateTimeFormat("en-GB", {
    timeZone: "Asia/Shanghai",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).format(now);
  const currentPeriod = periods.findIndex(
    (p) => p.start <= time && time < p.end,
  );
  const next = periods.find((p) => p.start > time);
  return {
    ...c,
    timetable: periods.map((p) => p.subject),
    currentPeriod,
    nextPeriod:
      currentPeriod < 0 && next ? `下一节 ${next.subject} ${next.start}` : "",
    timetableState:
      template || override?.mode === "closed" || override?.mode === "custom"
        ? "configured"
        : "pending",
  };
}
