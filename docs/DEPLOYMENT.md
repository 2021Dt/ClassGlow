# 服务器部署与手机使用

本方案面向一台 Linux 服务器，使用 Docker Compose、Caddy、FastAPI 和持久化 SQLite。前端已经是响应式网页，电脑和手机使用同一个 HTTPS 地址。Windows 客户端安装在教室电脑，服务器上不运行桌面客户端。

本项目域名：`teacher.dgplus.vip`。代码仓库：[2021Dt/ClassGlow](https://github.com/2021Dt/ClassGlow)，部署分支为 `main`。以下脚本面向 Ubuntu 22.04/24.04/26.04 或 Debian 12/13、使用 systemd 的独立服务器。教师与管理员操作见 [使用说明](USAGE.md)。

## 106 服务器交接记录

记录日期：2026-11-03。本机已按“已有 Nginx 的共享服务器”方案部署，日常使用 `sudo classglow` 运维命令即可，不要重复执行独立服务器安装步骤。

| 项目 | 已部署配置 |
| --- | --- |
| 入口 | `https://teacher.dgplus.vip/`；客户端服务地址 `https://teacher.dgplus.vip/api/` |
| 服务器与代码 | `106.53.47.8`；`/opt/classglow`；跟踪 GitHub `main` |
| 最近应用发布记录 | `b8fd180f8a5771165c1a974e988a7873ac9dda53`；实际当前提交通过 `sudo classglow status` 查看 |
| 请求链路 | 宿主机 Nginx HTTPS → `127.0.0.1:18080` → Web 容器 Caddy → 内网 API |
| 数据 | SQLite `/app/data/kanban.db`，位于 `classroom-board_app_data` 卷 |
| 配置 | `/opt/classglow/deploy/.env`，由 root 管理，权限 0600，不进入 Git |
| 自动更新 | 已开启 `classglow-update.timer`，每 5 分钟检查新提交；以 `sudo classglow auto-status` 为准 |
| 镜像构建 | 使用 Docker 官方 ECR 镜像；`KANBAN_BUILD_NETWORK=host` 仅用于构建，运行时仍为隔离网络 |
| HTTPS | 沿用宿主机 Certbot；本次证书到期日 2027-02-01，续期由原有定时器处理 |
| Windows 发布 | 0.2.0 当前用户安装包，位于 `.deploy/downloads`，详见文末下载与校验值 |

本次发布已验证首页、健康接口、客户端版本接口及安装包下载；无效凭证访问设备卸载接口返回 401。没有为生产环境创建默认管理员或演示数据。首次管理员初始化由用户持安装密钥在网页完成。

停用、日常空间清理和彻底移除的顺序见 [服务器后期清理](SERVER-CLEANUP.md)。两份文档随代码存放在服务器 `/opt/classglow/docs/`，不包含安装密钥、口令或私钥。

## 一键安装与持续更新

先把域名 A 记录指向服务器公网 IP，开放 TCP 80/443，确认没有其他 Nginx、宝塔或网关占用这两个端口。服务器需要能访问 GitHub、Docker 软件源及镜像源。执行：

```bash
curl -fsSL https://raw.githubusercontent.com/2021Dt/ClassGlow/main/deploy/install.sh -o classglow-install.sh && sudo bash classglow-install.sh
```

脚本会安装 Git、Docker 和 Compose，克隆代码到 `/opt/classglow`，创建仅服务器保留的 `deploy/.env`，构建前后端镜像，然后停服、备份已有 SQLite、执行 Alembic 迁移、启动服务，最后检查 HTTPS 首页和 API。新安装没有数据库时跳过备份。Docker 安装采用官方 [Ubuntu](https://docs.docker.com/engine/install/ubuntu/) / [Debian](https://docs.docker.com/engine/install/debian/) 软件源；已安装的 Docker 不主动升级或卸载。

目前仓库公开，服务器默认通过 HTTPS 只读拉取，不需要 GitHub 私钥。如果以后将仓库设为私有，先为服务器配置只读 Deploy Key，再将 `/opt/classglow` 的 `origin` 改为 `git@github.com:2021Dt/ClassGlow.git`；首次 SSH 连接需在终端核对 GitHub 主机指纹。无需向服务器复制开发者私钥。

安装成功后，通过 `sudo classglow setup-key` 单独读取一次性安装密钥，再打开 `https://teacher.dgplus.vip/` 完成初始化。密钥不会写入安装与自动更新日志。管理员账号和口令在网页上自行设置，没有默认口令。安装命令可再次运行，已有配置和数据卷保留。

```bash
# 手工一键更新：拉取 main、构建、备份、迁移、部署
sudo classglow update

# 开启每 5 分钟检查新提交并自动部署；服务器重启后仍有效
sudo classglow auto-on

# 查看当前服务、更新日志和定时器
sudo classglow status
sudo classglow auto-status
sudo classglow logs

# 停止后续自动更新；已经开始的一次更新会完成
sudo classglow auto-off

# 重新读取安装密钥，或手工在线备份
sudo classglow setup-key
sudo classglow backup
```

“自动整合”是对 `origin/main` 进行快进更新，再一起构建前后端。服务器代码存在未提交修改、本地额外提交或分支分歧时停止，不自动猜测冲突解决方案；服务器配置放入忽略的 `deploy/.env`，业务数据库保存在 Docker 卷。自动更新只在提交改变时部署，手工 `update` 则会重建，也用于配置修改或失败后的重试。脚本拉取后执行新提交中的部署逻辑。

更新先构建，构建期间旧服务继续运行；之后存在备份、迁移和启动造成的短暂停服。自动更新默认关闭，开启后 `main` 的新提交最多在下一次 5 分钟检查时触发更新。持续展示的教室客户端会使用本机缓存。

备份位于数据卷 `/app/data/backups/backup-before-deploy-*.db`，也可在“系统管理 → 备份恢复”下载。不会自动删除历史备份，需定期下载到服务器外并按学校保留策略清理。部署成功提交号保存在 `/opt/classglow/.deploy/deployed-commit`。失败时保留 `/opt/classglow/.deploy/failed` 和日志，后续定时部署暂停；排查后运行 `sudo classglow update` 重试。构建失败时旧服务保留；迁移阶段失败时保持停服。脚本不自动降级数据库或删除数据卷。

一键脚本当前针对 SQLite。将来切换 PostgreSQL 时按第 5 节的备份、迁移流程操作，再调整部署脚本的数据库备份步骤。若服务器已有统一网关，使用下面的 Compose 配置作为基础接入现有网关，不能直接运行占用 80/443 的独立安装方案。

## 1. 准备服务器

### 已有 Nginx 的共享服务器

Docker 仅在服务器运行，开发电脑无需启动 Docker。现有 Nginx 占用 80/443 时，使用：

```bash
curl -fsSL https://raw.githubusercontent.com/2021Dt/ClassGlow/main/deploy/install.sh -o classglow-install.sh
sudo bash classglow-install.sh --existing-nginx
```

此入口复用已安装的 Docker、Compose（≥ 2.24.4）、Git、Nginx 与已注册账户的 Certbot，不安装或升级系统包，不重启 Docker。只增加 `teacher.dgplus.vip` 站点；同名非本项目配置存在时会停止，须先人工核对。域名仍须指向本机，HTTP 的 ACME 验证路径须可从公网访问。

`deploy/.env` 中设置 `KANBAN_GATEWAY=nginx` 后，运维命令自动叠加 `compose.nginx.yaml`：网页只发布 `127.0.0.1:18080`，API 不发布主机端口，Caddy 在容器内处理网页和 `/api/` 转发，宿主机 Nginx 负责 HTTPS。Nginx 覆盖客户端自带的转发头；Caddy 仅在此模式信任内网代理。证书续期沿用宿主机 Certbot 定时器。

Docker Hub 无法访问时，可在 `deploy/.env` 设置 `PYTHON_IMAGE`、`NODE_IMAGE`、`CADDY_IMAGE` 为示例文件中的 Amazon ECR 地址。这些是 [Docker 官方公开镜像](https://gallery.ecr.aws/docker/library/python)，仅影响本项目构建，不修改 Docker 全局镜像源或代理。

如果宿主机下载正常、Docker 桥接网络中的大文件下载持续停滞，可在本项目 `deploy/.env` 设置 `KANBAN_BUILD_NETWORK=host`。该选项只让镜像构建步骤使用宿主机网络，不修改 Docker 全局配置，应用启动后仍使用 Compose 的隔离网络。Python 时区数据由锁定的 `tzdata` 依赖提供，构建无需额外下载 Debian 系统包索引，符合 [Python 的时区数据查找机制](https://docs.python.org/3/library/zoneinfo.html#data-sources)。

本模式新增的服务器内容如下；其他站点与 Docker 项目保持原配置：

| 位置 | 用途 |
| --- | --- |
| `/opt/classglow` | Git 源码、本项目配置与部署状态 |
| `/opt/classglow/.deploy/downloads` | 可选 Windows 客户端下载文件 |
| `/etc/nginx/sites-available/teacher.dgplus.vip`、`sites-enabled/teacher.dgplus.vip` | 指向本项目 Nginx 配置的链接 |
| `/etc/letsencrypt/live/teacher.dgplus.vip` | Certbot 管理的域名证书，相关续期记录也由 Certbot 管理 |
| `/usr/local/bin/classglow` | 运维命令入口 |
| `/etc/systemd/system/classglow-update.{service,timer}` | 开启自动更新后创建的定时任务 |
| `classroom-board_*` Docker 卷与网络 | 项目持久数据与隔离网络 |

所有日常操作仍使用 `sudo classglow update/status/logs/backup/setup-key/auto-on/auto-off`（每次填写一个子命令）。需要直接运行 Compose 时须同时加 `-f compose.production.yaml -f compose.nginx.yaml`，否则会使用独立服务器的 80/443 端口配置。

以下命令在服务器的项目根目录执行。服务器需要 Docker Engine 和 Docker Compose 插件，安装方式见 [Docker 官方安装文档](https://docs.docker.com/engine/install/)。小规模试点可先按 2 核、2GB 内存准备；这是起步配置，尚未进行容量压测。

准备域名，例如 `teacher.dgplus.vip`，将 A 记录指向服务器公网 IP；若设置 AAAA，IPv6 也须能到达服务器。开放 TCP 80、443，SSH 按运维需要配置。不需要开放 5173 或 8000。Caddy 在域名解析正确且端口可达时申请并续期 HTTPS 证书，依据 [Caddy 自动 HTTPS 文档](https://caddyserver.com/docs/automatic-https)。

上传项目源代码，包含 `backend/`、`frontend/`、`scripts/`、`deploy/`、`pyproject.toml`、`uv.lock`、`alembic.ini`、`README.md`、`.dockerignore` 和 `compose.production.yaml`。不上传 `.venv`、`node_modules`、`.qa`、`.tools`、个人 `.env` 或开发数据库。已有数据按第 5 节导入。

## 2. 手动部署（已使用一键脚本时跳过这些安装步骤）

以下复制 `.env` 的操作仅用于新安装。106 服务器已经有配置，不能覆盖它。

```bash
cp deploy/.env.example deploy/.env
chmod 600 deploy/.env
nano deploy/.env
```

将 `KANBAN_DOMAIN` 改为自己的域名，只填写域名。SQLite 配置保持：

```dotenv
KANBAN_DOMAIN=teacher.dgplus.vip
KANBAN_DATABASE_URL=sqlite:////app/data/kanban.db
KANBAN_ALLOW_SQLITE_PRODUCTION=true
```

手工操作统一使用下面的 Bash 函数。它和运维脚本一样读取网关模式，自动为共享服务器叠加 Nginx 配置，避免错误占用 80/443。后续第 4、5 节也使用 `dc`；重新登录终端后需重新定义：

```bash
cd /opt/classglow
dc() {
  local files=(-f compose.production.yaml)
  if sudo grep -qx 'KANBAN_GATEWAY=nginx' deploy/.env; then
    files+=(-f compose.nginx.yaml)
  fi
  sudo docker compose --env-file deploy/.env "${files[@]}" "$@"
}
```

新安装继续执行：

```bash
dc config --quiet
dc up -d --build
dc ps -a
dc logs --tail=80 api web migrate
curl --fail https://teacher.dgplus.vip/api/health
```

`migrate` 一次性退出且状态为 0 是正常现象；它完成 Alembic 迁移后，`api` 健康检查通过，才启动 `web`。此顺序使用 [Compose 的完成与健康条件](https://docs.docker.com/compose/how-tos/startup-order/)。网页、接口和 TLS 使用同一域名；API 只在内部网络开放。应用与 Caddy 都没有启用记录完整请求路径的访问日志，以免邀请秘密进入日志。

服务启动后，读取首次安装密钥：

```bash
dc exec api python -m scripts.setup_key
```

打开网站的初始化向导，填写安装密钥、站点名称、管理员账号和口令。默认关闭教师自行注册，初始化完成后进入“系统管理 → 账号管理”创建教师账号，也可以在“站点设置”开放注册。初始化只允许成功一次，完成后即锁定；密钥存放在数据卷中的 `setup-key.txt`，不会通过网页或启动日志返回。

本机开发用 `uv run python -m scripts.setup_key` 读取。已有旧版数据升级后同样需要首次设置系统管理员：可以新建管理账号，或填写已有账号及其原口令进行升级，历史业务数据保留。

数据保存在 `classroom-board_app_data` 命名卷。独立网关模式的证书保存在 Caddy 卷；106 的共享 Nginx 模式由宿主机 Certbot 保存证书。API 容器以普通用户运行、根文件系统只读，只有数据卷和临时目录可写。SQLite 使用一个 API 进程，暂不横向扩容，也不将数据库文件放入 NFS/共享盘。不要运行 `docker compose down -v`，该参数会删除数据卷。

如果已有 Nginx，使用第 1 节的共享服务器接入步骤完成宿主机站点与证书配置。仅启动容器不会自动配置宿主机网关。宝塔等其他网关需要按其实际配置接入。

## 3. 手机与 Windows 客户端

| 使用位置 | 地址或操作 |
| --- | --- |
| 教师电脑、Android Chrome、iPhone Safari | 打开 `https://teacher.dgplus.vip/` 登录；注册入口由系统管理员控制 |
| 手机常用入口 | 底部“班级”“记录”“我的”；“我的”提供私人草稿、班级设置、大屏控制 |
| 系统管理员入口 | 电脑左侧“系统管理”；手机“我的 → 系统管理” |
| 手机展示布局 | 班级概览的展示预览入口；布局与主题须明确发布 |
| Windows 客户端服务地址 | **`https://teacher.dgplus.vip/api/`**，保留 `/api/` 前缀 |
| 绑定教室电脑 | 客户端显示 6 位码，教师登录网页核对设备和目标班级后确认 |

当前移动端为响应式 Web，不包含 Android APK、iOS App 或小程序。可以用浏览器的“添加到主屏幕”创建快捷入口；没有离线启动整个应用的 Service Worker。已打开页面在断网时保留未同步正文，恢复网络仅同步草稿，发布仍需用户确认。

手机里的 `127.0.0.1` 指手机自己，所以本机开发地址不能直接分享给手机。服务器部署后使用域名访问，无需与教室电脑同一 Wi-Fi。仅用局域网 HTTP IP 测试也可能缺少 `crypto.randomUUID()` 等安全上下文能力，正式测试应使用 HTTPS。

客户端默认关闭壁纸写入。先核对缓存与应用版本，再在测试电脑本机启用展示并做现场验收。响应式自动化验证不替代真实 iPhone、Android 的软键盘、扫码与浏览器兼容性验收。

## 4. 更新、备份与恢复

106 服务器日常更新使用 `sudo classglow update`，备份使用 `sudo classglow backup`。以下手工恢复或迁移前，先 `sudo classglow auto-off`，等当前部署结束，并协调其他运维人员暂停手工更新；完成核验后用 `sudo classglow auto-on` 恢复定时更新。第 2 节的 `dc` 函数必须先定义。

SQLite 现在可直接在“系统管理 → 备份恢复”创建备份、下载和恢复。网页恢复要求填写当前管理员口令和“恢复备份”，先自动备份当前数据库，再通过单个事务恢复数据。恢复成功后全部会话失效，使用备份中的管理员账号重新登录，教室设备重新绑定。网页恢复仅支持当前结构版本的本机备份；外部备份导入、旧版本备份迁移和 PostgreSQL 仍使用以下运维命令。

更新前先在线备份，复制到服务器外的受控备份位置。下例用固定输出名展示命令；每次换一个新名称，工具拒绝覆盖已有备份：

```bash
dc exec -T api \
  python -m scripts.sqlite_backup backup --output data/backups/before-update-20261102.db
sudo install -d -m 0700 backups
dc cp \
  api:/app/data/backups/before-update-20261102.db ./backups/
sudo chmod 600 backups/before-update-20261102.db
```

备份使用 SQLite 在线一致性 API，不能在写入过程中只复制主 `.db` 文件。备份包含业务数据和口令哈希，备份目录不可放在网站静态目录。建议每天执行并异机保留，恢复前核对备份时间。

上传更新后的代码后，停止服务再执行迁移，避免旧代码在迁移期间继续写入：

```bash
dc stop web api
dc build
dc run --rm --no-deps migrate
# 上一步成功才执行下一步；失败时保留停服状态，检查日志和备份。
dc up -d
curl --fail https://teacher.dgplus.vip/api/health
```

恢复使用一个新数据库文件，不覆盖当前文件：

```bash
dc stop web api
dc run --rm --no-deps api \
  python -m scripts.sqlite_backup restore \
  --source data/backups/before-update-20261102.db --output data/restored-20261102.db
```

将 `deploy/.env` 的数据库地址改成 `sqlite:////app/data/restored-20261102.db`，再执行 `dc up -d` 并检查健康。恢复会更换同步世代，旧会话和设备失效，需要重新登录、重新绑定，正式内容仍保留。若备份只在服务器外，先用 `dc cp` 将它送进停止状态的 `api:/app/data/backups/` 再恢复。

## 5. 导入现有 SQLite，或迁移 PostgreSQL

要把本机数据带到服务器，先在本机执行 `python -m scripts.sqlite_backup backup`，将生成的备份安全上传，再按第 4 节的“恢复”流程导入新库。这样不会把本机登录状态与教室绑定凭证直接带到公网环境。前端私人云草稿和正式内容在服务端备份内；尚未同步的浏览器本机输入需先同步。

未来切换 PostgreSQL：先备份并停服，准备空目标数据库，把目标连接串放进权限受限的 `deploy/.migration.env`：

```dotenv
KANBAN_MIGRATION_TARGET_URL=postgresql+psycopg://user:URL编码后的密码@数据库地址:5432/kanban
```

```bash
chmod 600 deploy/.migration.env
dc run --rm --no-deps \
  --env-from-file deploy/.migration.env api python -m scripts.migrate_database
```

如果当前 Compose 不支持 `run --env-from-file`，通过受保护的 shell 环境导出该变量并使用 `run -e KANBAN_MIGRATION_TARGET_URL`，不要把真实密码直接写在命令行。迁移工具要求目标完全为空，自己执行建表、逐表复制与核对。完成后再修改 `deploy/.env` 的 `KANBAN_DATABASE_URL`，设置 `KANBAN_ALLOW_SQLITE_PRODUCTION=false`，重新启动、登录、绑定。

默认内部网络禁止 API 直连外网。使用外部 PostgreSQL 时，需要在部署配置中为 `api` 和 `migrate` 增加可达数据库的专用网络；使用同机数据库容器则让其加入 `backend` 网络。不要为了连数据库而公开 API 的 8000 端口。PostgreSQL 真正切换前仍需在目标环境做一次迁移和并发演练。

## 6. 检查与故障定位

2026-11-03 已在 106 服务器完成 Linux Docker 镜像构建、SQLite 迁移、现有 Nginx 接入和公网 TLS 签发；`https://teacher.dgplus.vip/`、`/api/health`、`/api/setup/status` 已从服务器和开发电脑访问通过。证书有效期至 2027-02-01，续期由现有 Certbot 定时器负责。网页初始化尚未提交，没有预置管理员或演示数据。浏览器自动化在连接阶段超时，本次未进行线上登录后的交互复核；本地业务验证范围见 [验收记录](VERIFICATION.md)。

Windows x64 0.2.0 当前用户安装包提供[公网下载](https://teacher.dgplus.vip/downloads/ClassGlow-0.2.0-win-x64-setup.exe)，大小 51,280,494 字节，SHA-256 为 `b741f02c5da47aa4d6cbce55967c793c14d19b151ff78d6d68402edce9797ec2`。内置运行时，包含首次隐私告知和正式卸载入口，仍为未签名测试版。Windows 清理范围见 [隐私与卸载](WINDOWS-PRIVACY.md)，服务器停用和清理范围见 [服务器清理说明](SERVER-CLEANUP.md)。

| 现象 | 检查方向 |
| --- | --- |
| 首页能开，接口返回 HTML 或客户端不能配对 | 客户端地址是否带 `/api/`，代理是否去掉一次该前缀 |
| HTTPS 无法签发 | DNS A/AAAA、80/443、防火墙、已有网关占用，查看 web 日志 |
| API unhealthy | migrate 是否成功、数据卷权限、数据库地址、公开来源配置 |
| 手机显示连接失败 | 是否误用 `127.0.0.1`，是否使用有效 HTTPS 域名 |
| 保存冲突 | 保留本机输入，在页面比较最新版本；不要清浏览器存储来解决冲突 |
| 客户端已缓存但未应用 | 本机是否启用展示、是否暂停、是否仍有课件处于前台 |

可重复运行的移动端用例在 `frontend/e2e/mobile.pw.ts`：先在独立测试环境完成初始化并开放注册，安装前端依赖，执行 `npx playwright install chromium`，将 `KANBAN_TEST_URL` 指向该环境，再运行 `npm run test:e2e`。它会创建明确标为测试数据的账号与班级，不应定期对真实教学数据运行。
