import { createApp } from "vue";
import "./style.css";
import "./board.css";
import "./teacher.css";
import "./canvas.css";
import "./admin.css";

// 下载页无需登录，也不触发工作台的数据读取。
const entry = /^\/download\/?$/.test(location.pathname)
  ? import("./components/DownloadPage.vue")
  : /^\/studio\/?$/.test(location.pathname)
    ? import("./StudioPage.vue")
    : import("./App.vue");
entry.then(({ default: App }) => createApp(App).mount("#app"));
