import { createApp } from "vue";
import AppSelect from "./components/AppSelect";
import "./style.css";
import "./board.css";
import "./workspace-theme.css";
import "./select.css";
import "./glass.css";

// 下载页无需登录，也不触发工作台的数据读取。
const entry = /^\/download\/?$/.test(location.pathname)
  ? import("./components/DownloadPage.vue")
  : /^\/studio\/?$/.test(location.pathname)
    ? import("./StudioPage.vue")
    : import("./App.vue");
entry.then(({ default: App }) =>
  createApp(App).component("AppSelect", AppSelect).mount("#app"),
);
