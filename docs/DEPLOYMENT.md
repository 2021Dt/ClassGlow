# 服务器部署与手机使用

本方案面向一台 Linux 服务器，使用 Docker Compose、Caddy、FastAPI 和持久化 SQLite。前端已经是响应式网页，电脑和手机使用同一个 HTTPS 地址。Windows 客户端安装在教室电脑，服务器上不运行桌面客户端。

## 1. 准备服务器

以下命令在服务器的项目根目录执行。服务器需要 Docker Engine 和 Docker Compose 插件，安装方式见 [Docker 官方安装文档](https://docs.docker.com/engine/install/)。小规模试点可先按 2 核、2GB 内存准备；这是起步配置，尚未进行容量压测。

准备域名，例如 `board.example.com`，将 A 记录指向服务器公网 IP；若设置 AAAA，IPv6 也须能到达服务器。开放 TCP 80、443，SSH 按运维需要配置。不需要开放 5173 或 8000。Caddy 在域名解析正确且端口可达时申请并续期 HTTPS 证书，依据 [Caddy 自动 HTTPS 文档](https://caddyserver.com/docs/automatic-https)。

上传项目源代码，包含 `backend/`、`frontend/`、`scripts/`、`deploy/`、`pyproject.toml`、`uv.lock`、`alembic.ini`、`README.md`、`.dockerignore` 和 `compose.production.yaml`。不上传 `.venv`、`node_modules`、`.qa`、`.tools`、个人 `.env` 或开发数据库。已有数据按第 5 节导入。

## 2. 首次部署

```bash
cp deploy/.env.example deploy/.env
chmod 600 deploy/.env
nano deploy/.env
```

将 `KANBAN_DOMAIN` 改为自己的域名，只填写域名。SQLite 配置保持：

```dotenv
KANBAN_DOMAIN=board.example.com
KANBAN_DATABASE_URL=sqlite:////app/data/kanban.db
KANBAN_ALLOW_SQLITE_PRODUCTION=true
```

然后执行：

```bash
docker compose --env-file deploy/.env -f compose.production.yaml config --quiet
docker compose --env-file deploy/.env -f compose.production.yaml up -d --build
docker compose --env-file deploy/.env -f compose.production.yaml ps -a
docker compose --env-file deploy/.env -f compose.production.yaml logs --tail=80 api web migrate
curl --fail https://board.example.com/api/health
```

`migrate` 一次性退出且状态为 0 是正常现象；它完成 Alembic 迁移后，`api` 健康检查通过，才启动 `web`。此顺序使用 [Compose 的完成与健康条件](https://docs.docker.com/compose/how-tos/startup-order/)。网页、接口和 TLS 使用同一域名；API 只在内部网络开放。应用与 Caddy 都没有启用记录完整请求路径的访问日志，以免邀请秘密进入日志。

服务启动后，读取首次安装密钥：

```bash
docker compose --env-file deploy/.env -f compose.production.yaml exec api python -m scripts.setup_key
```

打开网站的初始化向导，填写安装密钥、站点名称、管理员账号和口令。默认关闭教师自行注册，初始化完成后进入“系统管理 → 账号管理”创建教师账号，也可以在“站点设置”开放注册。初始化只允许成功一次，完成后即锁定；密钥存放在数据卷中的 `setup-key.txt`，不会通过网页或启动日志返回。

本机开发用 `uv run python -m scripts.setup_key` 读取。已有旧版数据升级后同样需要首次设置系统管理员：可以新建管理账号，或填写已有账号及其原口令进行升级，历史业务数据保留。

数据保存在 `classroom-board_app_data` 命名卷，证书保存在独立 Caddy 卷。应用容器以普通用户运行、根文件系统只读，只有数据卷和临时目录可写。SQLite 使用一个 API 进程，暂不横向扩容，也不将数据库文件放入 NFS/共享盘。不要运行 `docker compose down -v`，该参数会删除数据卷。

如果已经运行宝塔、Nginx 或其他占用 80/443 的服务，需要先安排域名与端口接入，不能直接让两套网关争用端口。本文给的是独立服务器直接部署配置。

## 3. 手机与 Windows 客户端

| 使用位置 | 地址或操作 |
| --- | --- |
| 教师电脑、Android Chrome、iPhone Safari | 打开 `https://board.example.com/` 登录；注册入口由系统管理员控制 |
| 手机常用入口 | 底部“班级”“记录”“我的”；“我的”提供私人草稿、班级设置、大屏控制 |
| 系统管理员入口 | 电脑左侧“系统管理”；手机“我的 → 系统管理” |
| 手机展示布局 | 班级概览的展示预览入口；布局与主题须明确发布 |
| Windows 客户端服务地址 | **`https://board.example.com/api/`**，保留 `/api/` 前缀 |
| 绑定教室电脑 | 客户端显示 6 位码，教师登录网页核对设备和目标班级后确认 |

当前移动端为响应式 Web，不包含 Android APK、iOS App 或小程序。可以用浏览器的“添加到主屏幕”创建快捷入口；没有离线启动整个应用的 Service Worker。已打开页面在断网时保留未同步正文，恢复网络仅同步草稿，发布仍需用户确认。

手机里的 `127.0.0.1` 指手机自己，所以本机开发地址不能直接分享给手机。服务器部署后使用域名访问，无需与教室电脑同一 Wi-Fi。仅用局域网 HTTP IP 测试也可能缺少 `crypto.randomUUID()` 等安全上下文能力，正式测试应使用 HTTPS。

客户端默认关闭壁纸写入。先核对缓存与应用版本，再在测试电脑本机启用展示并做现场验收。响应式自动化验证不替代真实 iPhone、Android 的软键盘、扫码与浏览器兼容性验收。

## 4. 更新、备份与恢复

SQLite 现在可直接在“系统管理 → 备份恢复”创建备份、下载和恢复。网页恢复要求填写当前管理员口令和“恢复备份”，先自动备份当前数据库，再通过单个事务恢复数据。恢复成功后全部会话失效，使用备份中的管理员账号重新登录，教室设备重新绑定。网页恢复仅支持当前结构版本的本机备份；外部备份导入、旧版本备份迁移和 PostgreSQL 仍使用以下运维命令。

更新前先在线备份，复制到服务器外的受控备份位置。下例用固定输出名展示命令；每次换一个新名称，工具拒绝覆盖已有备份：

```bash
docker compose --env-file deploy/.env -f compose.production.yaml exec -T api \
  python -m scripts.sqlite_backup backup --output data/backups/before-update-20261102.db
mkdir -p backups
docker compose --env-file deploy/.env -f compose.production.yaml cp \
  api:/app/data/backups/before-update-20261102.db ./backups/
chmod 600 backups/before-update-20261102.db
```

备份使用 SQLite 在线一致性 API，不能在写入过程中只复制主 `.db` 文件。备份包含业务数据和口令哈希，备份目录不可放在网站静态目录。建议每天执行并异机保留，恢复前核对备份时间。

上传更新后的代码后，停止服务再执行迁移，避免旧代码在迁移期间继续写入：

```bash
docker compose --env-file deploy/.env -f compose.production.yaml stop web api
docker compose --env-file deploy/.env -f compose.production.yaml build
docker compose --env-file deploy/.env -f compose.production.yaml run --rm --no-deps migrate
# 上一步成功才执行下一步；失败时保留停服状态，检查日志和备份。
docker compose --env-file deploy/.env -f compose.production.yaml up -d
curl --fail https://board.example.com/api/health
```

恢复使用一个新数据库文件，不覆盖当前文件：

```bash
docker compose --env-file deploy/.env -f compose.production.yaml stop web api
docker compose --env-file deploy/.env -f compose.production.yaml run --rm --no-deps api \
  python -m scripts.sqlite_backup restore \
  --source data/backups/before-update-20261102.db --output data/restored-20261102.db
```

将 `deploy/.env` 的数据库地址改成 `sqlite:////app/data/restored-20261102.db`，再执行 `up -d` 并检查健康。恢复会更换同步世代，旧会话和设备失效，需要重新登录、重新绑定，正式内容仍保留。若备份只在服务器外，先用 `docker compose cp` 将它送进停止状态的 `api:/app/data/backups/` 再恢复。

## 5. 导入现有 SQLite，或迁移 PostgreSQL

要把本机数据带到服务器，先在本机执行 `python -m scripts.sqlite_backup backup`，将生成的备份安全上传，再按第 4 节的“恢复”流程导入新库。这样不会把本机登录状态与教室绑定凭证直接带到公网环境。前端私人云草稿和正式内容在服务端备份内；尚未同步的浏览器本机输入需先同步。

未来切换 PostgreSQL：先备份并停服，准备空目标数据库，把目标连接串放进权限受限的 `deploy/.migration.env`：

```dotenv
KANBAN_MIGRATION_TARGET_URL=postgresql+psycopg://user:URL编码后的密码@数据库地址:5432/kanban
```

```bash
chmod 600 deploy/.migration.env
docker compose --env-file deploy/.env -f compose.production.yaml run --rm --no-deps \
  --env-from-file deploy/.migration.env api python -m scripts.migrate_database
```

如果当前 Compose 不支持 `run --env-from-file`，通过受保护的 shell 环境导出该变量并使用 `run -e KANBAN_MIGRATION_TARGET_URL`，不要把真实密码直接写在命令行。迁移工具要求目标完全为空，自己执行建表、逐表复制与核对。完成后再修改 `deploy/.env` 的 `KANBAN_DATABASE_URL`，设置 `KANBAN_ALLOW_SQLITE_PRODUCTION=false`，重新启动、登录、绑定。

默认内部网络禁止 API 直连外网。使用外部 PostgreSQL 时，需要在部署配置中为 `api` 和 `migrate` 增加可达数据库的专用网络；使用同机数据库容器则让其加入 `backend` 网络。不要为了连数据库而公开 API 的 8000 端口。PostgreSQL 真正切换前仍需在目标环境做一次迁移和并发演练。

## 6. 检查与故障定位

本轮已通过生产模式 SQLite、本机 Caddy 的静态文件与 `/api/` 代理联调，以及 Compose 配置校验。本机 Docker Engine 未运行，尚未执行 Linux 镜像构建、公网证书签发或真实服务器部署，不能把配置校验等同于服务器验收。

| 现象 | 检查方向 |
| --- | --- |
| 首页能开，接口返回 HTML 或客户端不能配对 | 客户端地址是否带 `/api/`，代理是否去掉一次该前缀 |
| HTTPS 无法签发 | DNS A/AAAA、80/443、防火墙、已有网关占用，查看 web 日志 |
| API unhealthy | migrate 是否成功、数据卷权限、数据库地址、公开来源配置 |
| 手机显示连接失败 | 是否误用 `127.0.0.1`，是否使用有效 HTTPS 域名 |
| 保存冲突 | 保留本机输入，在页面比较最新版本；不要清浏览器存储来解决冲突 |
| 客户端已缓存但未应用 | 本机是否启用展示、是否暂停、是否仍有课件处于前台 |

可重复运行的移动端用例在 `frontend/e2e/mobile.pw.ts`：先在独立测试环境完成初始化并开放注册，安装前端依赖，执行 `npx playwright install chromium`，将 `KANBAN_TEST_URL` 指向该环境，再运行 `npm run test:e2e`。它会创建明确标为测试数据的账号与班级，不应定期对真实教学数据运行。
