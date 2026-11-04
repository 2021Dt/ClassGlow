import { watch, type ComputedRef } from "vue";
import { api, session } from "./api";
import { model } from "./store";
import { watchUpdates } from "./realtime";
import type { Classroom, Command, Device } from "./domain";

export function useDeviceFeed(
  classroom: ComputedRef<Classroom | undefined>,
  reload: () => Promise<void>,
) {
  watch(
    [() => session.token, () => classroom.value?.id],
    ([token, classId], _, cleanup) => {
      if (!token || !classId) return;
      let closed = false,
        busy = false,
        again = false;
      let debounce: ReturnType<typeof setTimeout>;
      async function read() {
        if (closed || document.visibilityState !== "visible") return;
        if (busy) {
          again = true;
          return;
        }
        busy = true;
        try {
          const result = await api<{
            devices: Device[];
            commands: Command[];
            classVersion: number;
          }>(`/classes/${classId}/devices`);
          if (closed || token !== session.token) return;
          const ids = new Set(
            model.devices.filter((d) => d.classId === classId).map((d) => d.id),
          );
          model.devices = [
            ...model.devices.filter((d) => d.classId !== classId),
            ...result.devices,
          ];
          model.commands = [
            ...model.commands.filter((c) => !ids.has(c.deviceId)),
            ...result.commands,
          ];
          if (classroom.value?.version !== result.classVersion) await reload();
        } catch {
          /* 保留最后确认的状态，普通接口负责登录失效处理。 */
        } finally {
          busy = false;
          if (again && !closed) {
            again = false;
            schedule();
          }
        }
      }
      function schedule() {
        clearTimeout(debounce);
        debounce = setTimeout(read, 120);
      }
      const disconnect = watchUpdates(
        `/classes/${classId}/device-events`,
        schedule,
      );
      const fallback = setInterval(read, 10000);
      document.addEventListener("visibilitychange", schedule);
      void read();
      cleanup(() => {
        closed = true;
        disconnect();
        clearInterval(fallback);
        clearTimeout(debounce);
        document.removeEventListener("visibilitychange", schedule);
      });
    },
    { immediate: true },
  );
}
