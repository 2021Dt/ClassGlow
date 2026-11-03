<script setup lang="ts">
import { onMounted, ref } from "vue";
import {
  ArrowDownToLine,
  ArrowRight,
  ChevronDown,
  Monitor,
  Copy,
  Check,
} from "@lucide/vue";

const release = ref<{ latestVersion: string; downloadUrl: string }>();
const loading = ref(true);
const error = ref("");
const copied = ref(false);
const shareError = ref("");

async function loadRelease() {
  loading.value = true;
  error.value = "";
  try {
    // 版本与地址由发布服务提供，分享页面链接后无需随版本重新发送。
    const response = await fetch("/api/client/version", {
      cache: "no-store",
      signal: AbortSignal.timeout(10000),
    });
    if (!response.ok) throw new Error();
    const data = await response.json();
    if (typeof data.latestVersion !== "string" || !data.downloadUrl)
      throw new Error();
    const url = new URL(data.downloadUrl, location.origin);
    if (!["https:", "http:"].includes(url.protocol)) throw new Error();
    release.value = {
      latestVersion: data.latestVersion,
      downloadUrl: url.href,
    };
  } catch {
    error.value = "暂时无法获取安装包，请稍后重试。";
  } finally {
    loading.value = false;
  }
}

async function copyLink() {
  shareError.value = "";
  try {
    await navigator.clipboard.writeText(
      new URL("/download", location.origin).href,
    );
    copied.value = true;
  } catch {
    shareError.value = "请复制浏览器地址栏中的链接。";
  }
}

onMounted(() => {
  document.title = "下载班级看板 · ClassGlow";
  void loadRelease();
});
</script>

<template>
  <div class="download-page">
    <header class="download-nav">
      <a class="download-brand" href="/" aria-label="ClassGlow 教师工作台">
        <img src="/app-icon.webp" width="34" height="34" alt="" />
        <span>ClassGlow</span>
      </a>
      <a class="workspace-link" href="/">教师工作台<ArrowRight :size="16" /></a>
    </header>

    <main class="download-main">
      <section class="download-hero" aria-labelledby="download-title">
        <div class="download-intro">
          <img
            class="download-icon"
            src="/app-icon.webp"
            width="100"
            height="100"
            alt="班级看板应用图标"
          />
          <p class="download-eyebrow">CLASSGLOW FOR WINDOWS</p>
          <h1 id="download-title">班级看板<br /><span>Windows 客户端</span></h1>
          <p class="download-description">
            作业、课表与提醒，<br class="mobile-break" />自动同步到教室电脑。
          </p>
          <div class="download-action">
            <a
              v-if="release"
              class="download-primary"
              :href="release.downloadUrl"
              download
            >
              <ArrowDownToLine :size="19" />下载 Windows 版
            </a>
            <button
              v-else
              class="download-primary"
              :disabled="loading"
              @click="loadRelease"
            >
              <ArrowDownToLine :size="19" />{{
                loading ? "正在获取安装包…" : "重新获取安装包"
              }}
            </button>
            <p v-if="error" class="download-error" role="alert">{{ error }}</p>
            <p v-else class="download-version" aria-live="polite">
              <template v-if="release"
                >v{{ release.latestVersion }}<span>·</span></template
              >Windows 64 位<span>·</span>内置运行环境
            </p>
          </div>
          <a class="install-anchor" href="#install"
            >如何安装<ChevronDown :size="15"
          /></a>
        </div>
        <div class="download-visual">
          <div class="download-halo" aria-hidden="true"></div>
          <img
            class="client-screenshot"
            src="/windows-client.png"
            width="720"
            height="640"
            alt="班级看板 Windows 客户端：连接班级、设置桌面展示与自动启动"
            fetchpriority="high"
          />
        </div>
      </section>

      <section
        id="install"
        class="install-section"
        aria-labelledby="install-title"
      >
        <div class="install-heading">
          <h2 id="install-title">三步，开始使用</h2>
          <span>在教室电脑上安装</span>
        </div>
        <ol class="install-steps">
          <li>
            <span class="step-number">1</span>
            <h3>下载并安装</h3>
            <p>
              打开安装包，选择安装位置。<br />从桌面或开始菜单打开「班级看板」。
            </p>
          </li>
          <li>
            <span class="step-number">2</span>
            <h3>连接你的班级</h3>
            <p>
              在客户端生成绑定码。班主任登录教师工作台，在「教室电脑」输入绑定码。
            </p>
          </li>
          <li>
            <span class="step-number">3</span>
            <h3>开启桌面展示</h3>
            <p>
              回到客户端，点「继续」，开启桌面展示并选择屏幕，然后点「完成」。
            </p>
          </li>
        </ol>
      </section>

      <section class="download-help" aria-label="常见问题">
        <details>
          <summary>手机上怎么使用？<ChevronDown :size="17" /></summary>
          <p>
            手机浏览器打开<a href="/">教师工作台</a
            >，使用管理员提供的账号和密码登录即可。Windows
            安装包用于教室电脑，手机无需安装。
          </p>
        </details>
        <details>
          <summary>关掉窗口后，还会同步吗？<ChevronDown :size="17" /></summary>
          <p>
            会。点击关闭或最小化后，程序收进右下角系统托盘，继续同步。点击托盘中的应用图标即可打开；如果看不到图标，展开托盘的隐藏图标区域。完全退出请右键图标，选择「退出并恢复原壁纸」。
          </p>
        </details>
        <details>
          <summary>怎样更新或卸载？<ChevronDown :size="17" /></summary>
          <p>
            首次安装可选择文件夹和桌面快捷方式。更新前，先从托盘退出程序，再运行新版安装包。升级保留原安装位置；换位置请先卸载旧版。卸载可点击客户端「卸载软件」，也可以在
            Windows「已安装的应用」中找到「ClassGlow
            班级看板」，或使用开始菜单中的「卸载班级看板」。卸载会清理本机数据，服务器上的班级内容保留。
          </p>
        </details>
        <details>
          <summary>安装提示未知发布者？<ChevronDown :size="17" /></summary>
          <p>
            当前版本尚未完成数字签名。学校统一管理的电脑，请联系管理员确认并协助安装。
          </p>
        </details>
      </section>
    </main>

    <footer class="download-footer">
      <span><Monitor :size="16" />ClassGlow · 班级看板</span>
      <div>
        <button @click="copyLink">
          <Check v-if="copied" :size="15" /><Copy v-else :size="15" />{{
            copied ? "链接已复制" : "复制下载页链接"
          }}
        </button>
        <p v-if="shareError" role="status">{{ shareError }}</p>
      </div>
    </footer>
  </div>
</template>

<style scoped>
.download-page {
  color: #1d1d1f;
  background: #fbfbfd;
  min-height: 100dvh;
  font-family:
    -apple-system, BlinkMacSystemFont, "Segoe UI", "Microsoft YaHei UI",
    sans-serif;
}
.download-page a {
  text-decoration: none;
}
.download-page a:focus-visible,
.download-page button:focus-visible,
.download-page summary:focus-visible {
  outline: 3px solid #0071e3;
  outline-offset: 5px;
}
.download-nav {
  max-width: 1168px;
  margin: auto;
  height: 80px;
  padding: 0 32px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid #e7e7eb;
}
.download-brand {
  display: flex;
  align-items: center;
  gap: 10px;
  color: #1d1d1f;
  font-size: 18px;
  font-weight: 650;
  letter-spacing: -0.5px;
}
.download-brand img {
  border-radius: 9px;
}
.workspace-link {
  color: #42424a;
  display: flex;
  gap: 8px;
  align-items: center;
  font-size: 13px;
}
.workspace-link:hover {
  color: #0071e3;
}
.download-main {
  max-width: 1168px;
  margin: auto;
  padding: 0 32px 40px;
}
.download-hero {
  display: grid;
  grid-template-columns: 0.95fr 1.05fr;
  gap: 56px;
  align-items: center;
  padding: 70px 0 82px;
}
.download-icon {
  border-radius: 24px;
  box-shadow:
    0 10px 28px #3525380f,
    0 0 0 1px #00000004;
  display: block;
}
.download-eyebrow {
  color: #797980;
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 2px;
  margin: 26px 0 12px;
}
.download-intro h1 {
  font-size: clamp(32px, 3.6vw, 46px);
  letter-spacing: -1.8px;
  line-height: 1.3;
  font-weight: 650;
}
.download-intro h1 span {
  color: #6e6e73;
  font-size: 0.83em;
  font-weight: 500;
  letter-spacing: -1px;
}
.download-description {
  color: #75757d;
  margin-top: 20px;
  font-size: 16px;
  line-height: 1.7;
}
.mobile-break {
  display: none;
}
.download-action {
  margin-top: 30px;
}
.download-primary {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  color: #fff;
  background: #0071e3;
  padding: 14px 25px;
  border-radius: 28px;
  font-size: 15px;
  font-weight: 500;
  transition: background 0.15s;
}
.download-primary:hover {
  background: #0077ed;
}
.download-primary:disabled {
  opacity: 0.6;
}
.download-version {
  font-size: 11px;
  color: #86868b;
  margin: 14px 0 0;
}
.download-version span {
  margin: 0 8px;
}
.download-error {
  font-size: 13px;
  color: #b93c3c;
  margin-top: 12px;
}
.install-anchor {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  color: #0071e3;
  font-size: 13px;
  margin-top: 22px;
}
.download-visual {
  position: relative;
  isolation: isolate;
  min-width: 0;
  padding: 24px 0;
}
.download-halo {
  position: absolute;
  z-index: -1;
  inset: -36px -16px;
  border-radius: 50%;
  background: radial-gradient(
    ellipse at 60% 55%,
    #eadde8a6,
    #e7edf54d 50%,
    transparent 70%
  );
}
.client-screenshot {
  width: 100%;
  height: auto;
  display: block;
  border-radius: 13px;
  box-shadow:
    0 32px 66px -30px #44394e60,
    0 0 0 1px #00000008;
}
.install-section {
  scroll-margin-top: 32px;
}
.install-heading {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: 24px;
}
.install-heading h2 {
  font-size: 26px;
  font-weight: 600;
  letter-spacing: -0.8px;
}
.install-heading > span {
  color: #86868b;
  font-size: 13px;
}
.install-steps {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 18px;
  list-style: none;
  margin: 0;
  padding: 0;
}
.install-steps li {
  background: #f1f2f5;
  border-radius: 20px;
  padding: 26px;
}
.step-number {
  color: #0071e3;
  display: inline-flex;
  width: 30px;
  height: 30px;
  background: #e5ebf5;
  border-radius: 50%;
  align-items: center;
  justify-content: center;
  font-size: 14px;
  font-weight: 600;
}
.install-steps h3 {
  margin: 17px 0 9px;
  font-size: 17px;
  font-weight: 600;
}
.install-steps p {
  color: #6e6e73;
  font-size: 13px;
  line-height: 1.9;
}
.download-help {
  max-width: 780px;
  margin: 52px auto 0;
}
.download-help details {
  border-bottom: 1px solid #e4e4e8;
}
.download-help summary {
  display: flex;
  justify-content: space-between;
  align-items: center;
  list-style: none;
  padding: 20px 0;
  cursor: pointer;
  font-size: 14px;
  font-weight: 500;
}
.download-help summary::-webkit-details-marker {
  display: none;
}
.download-help details[open] summary svg {
  transform: rotate(180deg);
}
.download-help p {
  padding: 0 28px 20px 0;
  font-size: 13px;
  line-height: 1.9;
  color: #6e6e73;
}
.download-help a {
  color: #0071e3;
  margin: 0 3px;
}
.download-footer {
  max-width: 1104px;
  margin: auto;
  padding: 24px 0 30px;
  border-top: 1px solid #e7e7eb;
  display: flex;
  align-items: center;
  justify-content: space-between;
  color: #86868b;
  font-size: 12px;
}
.download-footer > span,
.download-footer button {
  display: flex;
  align-items: center;
  gap: 7px;
}
.download-footer button {
  background: transparent;
  padding: 6px 0;
  color: #6e6e73;
}
.download-footer p {
  font-size: 12px;
}
@media (max-width: 780px) {
  .download-nav {
    height: 68px;
    padding: 0 24px;
  }
  .download-main {
    padding: 0 24px 30px;
  }
  .download-hero {
    grid-template-columns: 1fr;
    gap: 42px;
    padding: 42px 0 48px;
  }
  .download-intro {
    text-align: center;
  }
  .download-icon {
    margin: auto;
    width: 88px;
    height: 88px;
    border-radius: 22px;
  }
  .download-eyebrow {
    margin-top: 22px;
  }
  .download-intro h1 {
    font-size: 38px;
  }
  .download-description {
    font-size: 15px;
    margin-top: 16px;
  }
  .download-visual {
    max-width: 500px;
    margin: auto;
    padding: 0;
  }
  .download-halo {
    inset: -16px 0;
  }
  .install-heading h2 {
    font-size: 23px;
  }
  .install-heading > span {
    display: none;
  }
  .install-steps {
    grid-template-columns: 1fr;
    gap: 12px;
  }
  .install-steps li {
    position: relative;
    padding: 22px 22px 22px 68px;
  }
  .step-number {
    position: absolute;
    top: 22px;
    left: 22px;
  }
  .install-steps h3 {
    margin: 1px 0 8px;
  }
  .download-help {
    margin-top: 28px;
  }
  .download-footer {
    margin: 0 24px;
    gap: 14px;
    flex-wrap: wrap;
  }
}
@media (prefers-reduced-motion: reduce) {
  .download-primary {
    transition: none;
  }
}
</style>
