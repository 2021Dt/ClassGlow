const workspacePages = new Set([
  "overview",
  "contents",
  "drafts",
  "devices",
  "settings",
  "license",
  "system",
]);
export function readWorkspaceRoute(search: string) {
  const params = new URLSearchParams(search);
  const value = params.get("page") ?? "";
  return {
    page: workspacePages.has(value) ? value : "",
    classId: params.get("class") ?? "",
  };
}
export function workspaceSearch(search: string, page: string, classId: string) {
  const params = new URLSearchParams(search);
  params.set("page", workspacePages.has(page) ? page : "overview");
  if (classId) params.set("class", classId);
  else params.delete("class");
  return `?${params}`;
}

export function readRecordFilters(search: string) {
  const params = new URLSearchParams(search);
  const pick = (key: string, allowed: string[]) => {
    const value = params.get(`records_${key}`) ?? "";
    return allowed.includes(value) ? value : "";
  };
  const rawOffset = Number(params.get("records_offset") ?? 0);
  return {
    kind: pick("kind", ["homework", "todo"]),
    state: pick("state", [
      "active",
      "scheduled",
      "ended",
      "completed",
      "withdrawn",
    ]),
    subject: (params.get("records_subject") ?? "").slice(0, 20),
    date: /^\d{4}-\d{2}-\d{2}$/.test(params.get("records_date") ?? "")
      ? params.get("records_date")!
      : "",
    search: (params.get("records_search") ?? "").slice(0, 100),
    offset:
      Number.isSafeInteger(rawOffset) && rawOffset >= 0
        ? Math.floor(rawOffset / 20) * 20
        : 0,
  };
}
export function recordFiltersSearch(
  search: string,
  filters: ReturnType<typeof readRecordFilters>,
) {
  const params = new URLSearchParams(search);
  for (const [key, value] of Object.entries(filters)) {
    if (value) params.set(`records_${key}`, String(value));
    else params.delete(`records_${key}`);
  }
  return `?${params}`;
}
