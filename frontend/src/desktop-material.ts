import { createApp, h, nextTick, shallowRef } from "vue";
import CanvasBoard from "./components/CanvasBoard.vue";
import type { Classroom, Layout } from "./domain";
import "./board.css";
import "./canvas.css";
import "./glass.css";
import "./desktop-material.css";

type Job = { id: string; layout: Layout; width: number; height: number };
type Bridge = {
  postMessage: (message: object) => void;
  addEventListener: (
    event: string,
    handler: (event: MessageEvent<Job>) => void,
  ) => void;
};
const bridge = (window as unknown as { chrome: { webview: Bridge } }).chrome
  .webview;
const job = shallowRef<Job>();
createApp({
  render: () =>
    job.value
      ? h(CanvasBoard, {
          layout: job.value.layout,
          classroom: {
            name: "",
            message: "",
            event: "",
            eventDate: "",
            timetable: [],
          } as unknown as Classroom,
          contents: [],
          now: 0,
          materialOnly: true,
          outputSize: job.value,
        })
      : null,
}).mount("#app");

// 只接收布局外观，不传账号、设备凭证或作业正文。图片使用随布局传入的 data URL。
bridge.addEventListener("message", async ({ data }) => {
  try {
    job.value = data;
    await nextTick();
    const urls = [
      data.layout.backgroundImage,
      data.layout.panelImage,
      ...data.layout.canvas!.flatMap((item) => [
        item.image,
        item.backgroundImage,
      ]),
    ].filter(Boolean) as string[];
    await Promise.all(
      urls.map(
        (url) =>
          new Promise<void>((resolve, reject) => {
            const image = new Image();
            image.onload = () => resolve();
            image.onerror = () => reject(new Error("image"));
            image.src = url;
          }),
      ),
    );
    // 原库的 React effect 与 SVG 纹理需要先完成，随后让 Chromium 提交合成帧。
    await new Promise((resolve) => setTimeout(resolve, 400));
    await new Promise((resolve) =>
      requestAnimationFrame(() => requestAnimationFrame(resolve)),
    );
    bridge.postMessage({ id: data.id, ready: true });
  } catch {
    bridge.postMessage({ id: data.id, ready: false });
  }
});
bridge.postMessage({ loaded: true });
