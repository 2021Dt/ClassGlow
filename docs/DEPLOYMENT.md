# 服务器部署与手机使用

当前版本的事务、WAL 备份、两项新增迁移、素材兼容与发布恢复步骤见 [可靠性与发布说明](RELIABILITY.md)。自动更新现按稳定标签和北京时间维护窗口执行，默认 02:00–05:00；本文后面的旧版本部署记录保留作历史证据。

本方案面向一台 Linux 服务器，使用 Docker Compose、Caddy、FastAPI 和持久化 SQLite。前端已经是响应式网页，电脑和手机使用同一个 HTTPS 地址。Windows 客户端安装在教室电脑，服务器上不运行桌面客户端。

本项目域名：`teacher.dgplus.vip`。代码仓库：[2021Dt/ClassGlow](https://github.com/2021Dt/ClassGlow)，部署分支为 `main`。以下脚本面向 Ubuntu 22.04/24.04/26.04 或 Debian 12/13、使用 systemd 的独立服务器。教师与管理员操作见 [使用说明](USAGE.md)。

2026-11-05 增加每日存储维护与管理员永久删除入口。部署会安装 `classglow-maintenance.timer`，北京时间 03:30 起的 5 分钟内清理 30 天前日志和失效记录并按需回收 SQLite 空间，手工执行使用 `sudo classglow cleanup`。安装包目录 `.deploy/downloads` 的目录所有者为 10001，并挂入 API 供管理员删除旧包。106 已启用 `KANBAN_JOURNAL_LOGS=true`，容器日志进入宿主机 journal，宿主机策略为最多 30 天、持久日志目标上限 512 MB。具体保留规则与删除范围见 [存储维护](SERVER-CLEANUP.md)。

## 106 服务器交接记录

记录日期：2026-11-06。本机已按“已有 Nginx 的共享服务器”方案部署，日常使用 `sudo classglow` 运维命令即可，不要重复执行独立服务器安装步骤。

| 项目 | 已部署配置 |
| --- | --- |
| 入口 | `https://teacher.dgplus.vip/`；客户端服务地址 `https://teacher.dgplus.vip/api/` |
| 服务器与代码 | `106.53.47.8`；`/opt/classglow`；跟踪 GitHub `main` |
| 最近应用发布记录 | 0.4.16，桌面时钟独立调度与持续校时；稳定标签 `v0.4.16`，应用提交 `5c3a0f9`，准确部署记录通过 `sudo classglow status` 查看 |
| 请求链路 | 宿主机 Nginx HTTPS → `127.0.0.1:18080` → Web 容器 Caddy → 内网 API |
| 数据 | SQLite `/app/data/kanban.db`，位于 `classroom-board_app_data` 卷 |
| 配置 | `/opt/classglow/deploy/.env`，由 root 管理，权限 0600，不进入 Git |
| 自动更新 | 已开启 `classglow-update.timer`，每 5 分钟检查稳定版本标签，仅在北京时间 02:00–05:00 自动部署；以 `sudo classglow auto-status` 为准 |
| Git 网络 | 仅本仓库 `.git/config` 的 `http.proxy` 使用服务器现有 `http://127.0.0.1:7890`；直连 GitHub 超时，现有代理可正常读取 main |
| 镜像构建 | 使用 Docker 官方 ECR 镜像；`KANBAN_BUILD_NETWORK=host` 仅用于构建，运行时仍为隔离网络 |
| HTTPS | 沿用宿主机 Certbot；本次证书到期日 2027-02-01，续期由原有定时器处理 |
| Windows 发布 | 0.4.16 当前用户安装包，位于 `.deploy/downloads`，下载与校验值见下方本次发布记录 |

本次发布已验证首页、健康接口、客户端版本接口及安装包下载；无效凭证访问设备卸载接口返回 401。没有为生产环境创建默认管理员或演示数据。首次管理员由服务器菜单创建，网站不展示注册或安装入口。

### 0.4.16 更新记录

2026-11-06 北京时间 10:26，106 更新到稳定标签 `v0.4.16`、应用提交 `5c3a0f9984f0130a9e43561d95b7d290a052298d`。Windows 与 Web/API 的 [远端 CI](https://github.com/2021Dt/ClassGlow/actions/runs/42149504148) 全部通过后发布。首次拉取 GitHub 遇到 TLS 中断，发生在停服前；重试仅为本次 Git 请求指定 HTTP/1.1，未关闭 TLS 校验或修改服务器全局代理。

在线备份为 `/app/data/backups/backup-before-deploy-20261106T022251Z-55a1c715.db`，停写备份为 `/app/data/backups/backup-before-deploy-20261106T022621Z-180285a0.db`，均为 0600；配置备份为 `/opt/classglow/.deploy/env-before-0.4.16-20261106`。本次没有新增数据库迁移，结构继续为 `f4859e63cb27`。账号 2、班级 1、成员关系 1、正式内容 3、修订 4、草稿 23、设备 4，均与更新前一致；设备绑定及凭证摘要一致，同步世代保留。数据库完整性、外键检查、API 健康、Nginx 与两个维护定时器均正常。

服务端为 200/304 快照响应补充校时时间；主要停更修复在 Windows 客户端。公网首页、版本与下载接口、静态资源、未登录接口保护、390px 下载页及完整安装包校验通过。下载接口同时报告 `latestVersion=0.4.16`、`serverVersion=0.4.16`。

Windows x64 0.4.16 [安装包下载](https://teacher.dgplus.vip/downloads/ClassGlow-0.4.16-win-x64-setup.exe)，大小 53,744,440 字节，SHA-256 为 `a0643b89d21c86a0a63e8d721edf741745946075af1e63a929d246c00fd4898f`；本地、服务器及公网完整下载摘要一致。

客户确认由现场人员安装。步骤：从托盘退出旧客户端，保持原安装目录覆盖安装，完成后手动打开；若提示隐私说明则现场确认，再检查版本为 0.4.16 并恢复展示。保留原绑定和设置。部署后核对时在线客户端仍为 0.4.11，尚不能宣称客户现场时钟故障已消除。普通窗口有焦点但时钟可见、跨分钟、跨日事件倒计时与课堂秒级倒计时的验收范围见 [验证记录](VERIFICATION.md)。

### 0.4.15 更新记录

2026-11-05 北京时间 17:16，106 从 `c75254c` 更新到应用提交 `132f4f2`。先构建并检查前后端镜像，再停服备份、执行两项 Alembic 迁移并切换服务；当前数据库结构为 `f4859e63cb27`。本次停写备份为容器内 `/app/data/backups/backup-before-deploy-20261105T091618Z-435e9f1b.db`，先前在线备份为 `backup-before-deploy-20261105T091155Z-ad1ee274.db`，两份备份权限均为 0600。

迁移后账号 2、班级 1、成员关系 1、正式内容 3、修订 4、草稿 23、设备 4，与发布前一致；SQLite `quick_check` 为 `ok`、外键检查无异常、WAL 已启用，同步世代保持。已有设备继续提交心跳并取得 200/304 快照响应。HTTPS 首页、下载页、入口 JS/CSS、健康和版本接口均通过公网检查；未登录访问会话、诊断指标、设备素材和工作台数据均返回 401。自动更新与每日清理定时器已恢复为 active/enabled；维护窗口外的自动部署实际跳过，宿主机 Nginx 配置检查通过。

Windows x64 0.4.15 [安装包下载](https://teacher.dgplus.vip/downloads/ClassGlow-0.4.15-win-x64-setup.exe)，大小 53,726,326 字节，SHA-256 为 `bd7282e7dfa821c612f104e7cbe4f59c22801d2e88a5d5e5c3f984efb1dbf57e`。本地与服务器全文件摘要一致，公网 Range 返回 206、文件头和总长度正确。教室电脑需退出旧客户端后覆盖安装，保留绑定与设置；服务器发布不会替换电脑上的程序。新版本的实体教室长时间运行及完整安装卸载验收仍待执行，范围见 [验证记录](VERIFICATION.md)。

0.4.6 沿用 SQLite，迁移到 `d1529b3fc804`，新增 `device_displays` 表。每台设备最多保留一张 400 KB 的 JPEG 回传图及状态，没有新增上传目录、服务或端口。布局接口上限为 2 MiB，回传接口为 600,000 字节，其他接口保持 128 KiB；已有 Nginx/Caddy 的 2 MiB 网关上限可继续使用。普通班级成员可查看本班回传，远程控制仍按角色和高级许可限制。客户端需手动升级到 0.4.6 并确认修订后的隐私说明，服务端更新不会自动替换教室电脑程序。

部署脚本在迁移前停止本项目 API 与 Web 并保留数据库备份；原有账号、班级和设备绑定不因本次新增表迁移而失效。旧结构备份仍可下载、查看数量或删除；恢复前需要先在隔离副本上迁移到当前结构，不要直接覆盖在线数据库。恢复和数据库迁移会清除当前回传，并维持原有重新登录、重新绑定要求。

停用、日常空间清理和彻底移除的顺序见 [服务器后期清理](SERVER-CLEANUP.md)。两份文档随代码存放在服务器 `/opt/classglow/docs/`，不包含安装密钥、口令或私钥。

### 0.4.11 更新说明

本次数据库结构仍为 `a913c07b5e2f`，只更新网页、版本信息和 Windows 安装包，不新增服务或端口。网页与 Windows 离线材质共用去外阴影样式。客户端发现 Wallpaper Engine 时在本机提示先退出，暂缓壁纸应用；同步继续，回传不会把被遮住的缓存图当作当前展示。

先上传并校验下方安装包，再修改 `KANBAN_CLIENT_DOWNLOAD_URL`，最后推送 main 并执行 `sudo classglow update`（已启用定时更新的服务器也会自动拉取）。默认版本为 `0.4.11`。必须先准备安装包，避免定时更新先发布了不存在的下载地址。

教室电脑应从托盘退出旧版，直接覆盖安装到原位置；保留绑定和设置，不能先卸载。隐私修订号为 `2026-11-04.3`，首次手动启动需重新确认。详细检测范围及卸载边界见 [Windows 隐私与卸载](WINDOWS-PRIVACY.md)。

### 0.4.10 更新（历史）

本次数据库结构仍为 `a913c07b5e2f`，不新增服务器服务或端口。Windows 自由画布用现有 WebView2 加载随安装包分发的 `Renderer/desktop.html`，直接使用网页同一套 CanvasBoard 与 Liquid Glass React 生成材质底图。仅布局、图片、材质尺寸或程序版本改变时重新生成；底图最高为 1920×1080，4K 屏幕复用同等比例的材质缓存，正文仍按实际分辨率绘制。作业、时钟和翻页不重新启动浏览器。缺少 WebView2、超大分辨率、关闭透明或减少效果时保留兼容渲染，不自动安装运行时。

客户端设置窗口保持软件绘制，第一帧显示后才应用系统支持的云母；高对比、远程桌面或减少效果时回退普通背景。隐私修订为 `2026-11-04.2`，升级需要手动打开并重新同意。服务器更新不会自动替换教室电脑上的程序。

打包前在 `frontend` 执行 `npm ci`，再运行 `python scripts/package_windows.py`；脚本自动构建离线材质入口、复制资源和第三方许可并生成逐文件卸载清单。发布时先上传并校验新包，再修改 `KANBAN_CLIENT_DOWNLOAD_URL` 后运行 `sudo classglow update`。版本接口由项目默认版本 `0.4.10` 提供，本机上报版本来自程序集。

本机现有 Mihomo 代理只用于仓库 Git 网络，没有新增代理服务，也不改变教师客户端或系统全局代理。若以后停用该代理，先确认服务器能直连 GitHub，再在 `/opt/classglow` 执行 `sudo git config --local --unset http.proxy`；直接移走代理会使定时拉取失败。

### 0.4.9 更新（历史）

本次数据库结构继续使用 `a913c07b5e2f`，组件独立寄语和倒计时保存在已有布局 JSON 中。部署仍先保存一致性备份，保留已有账号、班级和设备绑定。底板铺满显示器，内部画布使用完整区域；旧设计的组件坐标保留，显示范围会扩展至完整底板。

教室电脑需退出旧客户端，安装 0.4.9 才能使用新的排版、独立寄语、倒计时与首次同步修复。更新服务器不会自动安装客户端。下载页保留更新日志与下载限流，并展示作者 `by @dp`。

发布列表新增永久删除，按原有班级、角色和内容版本检查；同时移除对应修订历史并推进班级同步版本。旧数据库备份中的内容不会随在线删除而清除，备份管理见清理文档。

网页直接集成 `liquid-glass-react@1.1.1`、Magic UI 官方 BentoGrid / MagicCard 与 `reka-ui@2.11.0`。React 运行时与组件随 Vite 构建打包，不依赖运行时 CDN。依赖锁定在 `package-lock.json`，服务器继续使用 `npm ci`。浏览器折射使用 SVG / backdrop-filter，Safari 与 Firefox 的效果按原库支持情况降级；Windows 静态壁纸渲染仍与网页原库效果不同。

### 0.4.8 更新

此次前后端和 Windows 客户端共同发布。网页新增公告管理、教师工作台样式、组合画布、自由轮廓、动态文字、按学科筛选、卡片图片和字体设置。教室电脑需退出旧版，从下载页安装 0.4.8 后使用新增画布效果。网页更新无法替换本机客户端。

数据库从 `d1529b3fc804` 依次升级到 `e2637c41a905`、`a913c07b5e2f`，加入账号冻结字段、登录记录、密码恢复摘要和三张公告表。更新前由 `classglow update` 停写并保存一致性备份，原账号、班级、内容与设备绑定随迁移保留。当前结构可用 `sudo docker exec classroom-board-api-1 alembic current` 查看，部署备份可用 `sudo classglow backup-list` 查看。

管理员在网页“公告管理”编辑 Markdown、选择全体或指定账号并发布。每条公告的领取记录按账号保存，全体公告撤回前也覆盖新开通的账号。网页登录后与前台使用期间检查公告，每 30 秒检查一次；首次引导与其他弹窗结束后继续检查。公告显示在网页登录界面，Windows 教室展示保持教学内容。账号恢复与临时冻结的规则见 [账号与许可](ACCOUNT-LICENSES.md)。

## 一键安装与持续更新

先把域名 A 记录指向服务器公网 IP，开放 TCP 80/443，确认没有其他 Nginx、宝塔或网关占用这两个端口。服务器需要能访问 GitHub、Docker 软件源及镜像源。执行：

```bash
curl -fsSL https://raw.githubusercontent.com/2021Dt/ClassGlow/main/deploy/install.sh -o classglow-install.sh && sudo bash classglow-install.sh
```

脚本会安装 Git、Docker 和 Compose，克隆代码到 `/opt/classglow`，创建仅服务器保留的 `deploy/.env`，构建前后端镜像，然后停服、备份已有 SQLite、执行 Alembic 迁移、启动服务，最后检查 HTTPS 首页和 API。新安装没有数据库时跳过备份。Docker 安装采用官方 [Ubuntu](https://docs.docker.com/engine/install/ubuntu/) / [Debian](https://docs.docker.com/engine/install/debian/) 软件源；已安装的 Docker 不主动升级或卸载。

目前仓库公开，服务器默认通过 HTTPS 只读拉取，不需要 GitHub 私钥。如果以后将仓库设为私有，先为服务器配置只读 Deploy Key，再将 `/opt/classglow` 的 `origin` 改为 `git@github.com:2021Dt/ClassGlow.git`；首次 SSH 连接需在终端核对 GitHub 主机指纹。无需向服务器复制开发者私钥。

安装成功后运行 `sudo classglow` 打开交互菜单，选择“初始化管理员”，输入账号和两次密码（隐藏输入）。之后到网站正常登录。密码不会写入命令行参数、环境变量或日志，没有默认密码。管理员已初始化时可通过“重置管理员密码”恢复访问。重复运行安装不会覆盖配置或数据。

菜单提供状态、更新、管理员初始化/重置、数据库备份与位置、SQLite → PostgreSQL 迁移、自动更新、日志和卸载。常用无交互命令保留如下；不要让教师执行这些操作。

```bash
# 手工一键更新：拉取 main、构建、备份、迁移、部署
sudo classglow update

# 开启稳定标签检查；默认仅在北京时间 02:00–05:00 自动部署，重启后仍有效
sudo classglow auto-on

# 查看当前服务、更新日志和定时器
sudo classglow status
sudo classglow auto-status
sudo classglow logs

# 停止后续自动更新；已经开始的一次更新会完成
sudo classglow auto-off

# 创建首个管理员、重置已有管理员密码；均在终端隐藏输入
sudo classglow admin-init
sudo classglow admin-reset

# 备份、迁移向导和卸载向导
sudo classglow backup
sudo classglow backup-list
sudo classglow migrate-db
sudo classglow uninstall
```

手工 `update` 对 `origin/main` 进行快进更新，再一起构建前后端；`update --release v版本号` 指定稳定标签。自动更新只选择 main 历史中的稳定标签，并检查北京时间维护窗口。服务器代码存在未提交修改、本地额外提交或分支分歧时停止；服务器配置放入忽略的 `deploy/.env`，业务数据库保存在 Docker 卷。手工更新也可用于配置修改和失败后重试。

更新先构建并检查版本，期间旧服务继续运行；之后存在备份、迁移和启动造成的短暂停服。自动更新默认关闭，开启后每 5 分钟检查稳定标签，但只在允许窗口部署。默认北京时间 02:00–05:00，可用 `KANBAN_UPDATE_WINDOW` 调整；稳定标签应在 CI 通过后创建。持续展示的教室客户端使用本机缓存。

备份位于数据卷 `/app/data/backups/backup-before-deploy-*.db`，也可在“系统管理 → 备份恢复”下载。不会自动删除历史备份，需定期下载到服务器外并按学校保留策略清理。部署成功提交号保存在 `/opt/classglow/.deploy/deployed-commit`。失败时保留 `/opt/classglow/.deploy/failed` 和日志，后续定时部署暂停；排查后运行 `sudo classglow update` 重试。构建失败时旧服务保留；迁移阶段失败时保持停服。脚本不自动降级数据库或删除数据卷。

菜单支持从 SQLite 迁移到本项目私有网络内的 PostgreSQL 17，迁移成功后更新和备份命令自动切换数据库类型。外部 PostgreSQL 或跨服务器迁移仍按第 5 节人工核对配置。若服务器已有统一网关，使用下面的 Compose 配置作为基础接入现有网关，不能直接运行占用 80/443 的独立安装方案。

## 1. 准备服务器

### 服务器菜单与数据库迁移

日常运维只需 `sudo classglow`。菜单显示域名、运行状态、数据库类型、已部署提交和自动更新状态。管理员初始化/重置密码均使用隐藏输入；只处理已有系统管理员的密码，不在日志或命令参数中存储密码。

选择“迁移 SQLite → PostgreSQL”后，输入 `MIGRATE` 才开始。脚本在项目内网启动 PostgreSQL 17，不发布 5432 端口；先备份并停写，再向空库迁移、逐表核对、切换配置并等待 API 健康。原 SQLite、备份和 `.deploy/sqlite-before-postgres.env` 保留。数据库密码由程序随机生成，仅保存于 0600 配置文件。

迁移中的配置在 `.deploy/database-migration.env`，复制完成状态在 `.deploy/database-migration.state`。这些文件存在时自动更新暂停；再次选择迁移会使用同一目标继续，不重新生成数据库密码。复制失败会恢复原 SQLite 服务；复制完成后若新服务启动失败，则保留目标和状态，修复后重试，不盲目切回旧数据。成功后需要重新登录和绑定教室设备。

迁移后更新流程会先备份 PostgreSQL，再执行 Alembic。PostgreSQL 备份为 `.deploy/backups/*.dump`（0600），数据卷为 `classroom-board_postgres_data`；SQLite 备份仍在原数据卷 `/app/data/backups/`。`sudo classglow backup-list` 显示当前数据库的备份。两种备份均需另行复制到服务器外保存。

菜单目前只提供 SQLite → 本项目 PostgreSQL 的迁移。外部数据库、反向迁移和已有 PostgreSQL 库合并不自动执行。已经切换并产生新数据后，不能直接恢复旧 SQLite，否则会丢失切换后的更改。PostgreSQL 恢复应先停止自动更新、停写并备份当前库，使用 `pg_restore --single-transaction --clean --if-exists` 恢复指定备份，再运行 `scripts.rotate_epoch` 并启动服务；不要在运行中的库上直接恢复。

选择“卸载本项目”进入二级菜单，默认卸载服务并保留 `/opt/classglow`、数据卷、备份和下载包。彻底删除需要输入 `DELETE CLASSGLOW DATA`，将同时删除项目目录和项目数据卷；其他服务、Docker、Nginx、Certbot、宿主机 TLS 证书及共享构建缓存保留。详细范围和证书后续处理见 [清理文档](SERVER-CLEANUP.md)。

### 已有 Nginx 的共享服务器

Docker 仅在服务器运行，开发电脑无需启动 Docker。现有 Nginx 占用 80/443 时，使用：

```bash
curl -fsSL https://raw.githubusercontent.com/2021Dt/ClassGlow/main/deploy/install.sh -o classglow-install.sh
sudo bash classglow-install.sh --existing-nginx
```

此入口复用已安装的 Docker、Compose（≥ 2.24.4）、Git、Python 3、Nginx 与已注册账户的 Certbot，不安装或升级系统包，不重启 Docker。Python 3 仅使用标准库处理迁移配置。只增加 `teacher.dgplus.vip` 站点；同名非本项目配置存在时会停止，须先人工核对。域名仍须指向本机，HTTP 的 ACME 验证路径须可从公网访问。

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

所有日常操作使用 `sudo classglow` 菜单。需要直接运行 Compose 时，共享服务器须叠加 `compose.nginx.yaml`，迁移至本项目 PostgreSQL 后还须叠加 `compose.postgres.yaml`。建议使用下方的 `dc` 函数，避免遗漏配置。

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
  if sudo grep -qx 'KANBAN_DATABASE=postgres' deploy/.env; then
    files+=(-f compose.postgres.yaml)
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

服务启动后，运行 `sudo classglow`，选择“初始化管理员”。也可直接运行 `sudo classglow admin-init`。管理员密码隐藏输入，不通过网页或聊天传递。之后管理员正常登录网站，在“系统管理 → 创建账号”为老师设置账密、身份、班级和学科；教师无需注册。`/setup/initialize` 公网接口已移除。

本机开发使用 `uv run python -m scripts.admin_account init`。升级已有数据库时保留全部业务数据；尚未初始化的站点可在终端创建独立管理账号，已有管理员忘记密码则使用 `admin-reset`。

数据保存在 `classroom-board_app_data` 命名卷。独立网关模式的证书保存在 Caddy 卷；106 的共享 Nginx 模式由宿主机 Certbot 保存证书。API 容器以普通用户运行、根文件系统只读，只有数据卷和临时目录可写。SQLite 使用一个 API 进程，暂不横向扩容，也不将数据库文件放入 NFS/共享盘。不要运行 `docker compose down -v`，该参数会删除数据卷。

如果已有 Nginx，使用第 1 节的共享服务器接入步骤完成宿主机站点与证书配置。仅启动容器不会自动配置宿主机网关。宝塔等其他网关需要按其实际配置接入。

## 3. 手机与 Windows 客户端

| 使用位置 | 地址或操作 |
| --- | --- |
| 教师电脑、Android Chrome、iPhone Safari | 打开 `https://teacher.dgplus.vip/` 登录；账号和初始密码由管理员发放，老师直接登录 |
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

2026-11-03 已在 106 服务器完成 Linux Docker 镜像构建、SQLite 迁移、现有 Nginx 接入和公网 TLS 签发；`https://teacher.dgplus.vip/`、`/api/health`、`/api/setup/status` 已从服务器和开发电脑访问通过。证书有效期至 2027-02-01，续期由现有 Certbot 定时器负责。生产管理员由使用者在服务器菜单自行创建，没有预置管理员或演示数据。公网登录后的真实学校业务仍待开户后验收；本地隔离验证范围见 [验收记录](VERIFICATION.md)。

Windows x64 0.4.11 当前用户安装包提供[公网下载](https://teacher.dgplus.vip/downloads/ClassGlow-0.4.11-win-x64-setup.exe)，大小 53,726,644 字节，SHA-256 为 `5b34e7d00908b808d50352e639d24b1b848e040e49c00807b63ee7867da8620f`。内置 .NET 运行时；玻璃材质使用系统已有的 WebView2，缺失时使用兼容材质。包含首次隐私告知和正式卸载入口，仍为未签名测试版。Windows 清理范围见 [隐私与卸载](WINDOWS-PRIVACY.md)，服务器停用和清理范围见 [服务器清理说明](SERVER-CLEANUP.md)。

0.4.7 在创建首个窗口前启用本进程的软件绘制，启动/设置窗口不再使用 DWM 云母背景，取消正文从零透明度开始的动画。不会修改系统全局图形设置。启动异常通过系统对话框提示，并在原产品数据目录写入有限诊断；未出现异常的静默白屏不保证产生日志。2026-11-04 用户已在原故障 Windows 11 测试机更新到 0.4.7，并确认标题按钮和隐私引导页可以正常显示、操作，首次打开白屏复测通过。无需新的数据库迁移、服务或端口，原下载限流继续生效。

| 现象 | 检查方向 |
| --- | --- |
| 首页能开，接口返回 HTML 或客户端不能配对 | 客户端地址是否带 `/api/`，代理是否去掉一次该前缀 |
| HTTPS 无法签发 | DNS A/AAAA、80/443、防火墙、已有网关占用，查看 web 日志 |
| API unhealthy | migrate 是否成功、数据卷权限、数据库地址、公开来源配置 |
| 手机显示连接失败 | 是否误用 `127.0.0.1`，是否使用有效 HTTPS 域名 |
| 保存冲突 | 保留本机输入，在页面比较最新版本；不要清浏览器存储来解决冲突 |
| 客户端已缓存但未应用 | 本机是否启用展示、是否暂停、是否仍有课件处于前台 |

`frontend/e2e/mobile.pw.ts` 是旧版公开注册流程的历史用例，不适用于当前管理员开户、老师直接登录的入口。本次使用隔离账号验收新的身份导航和 390px 页面；后端关键权限与期限检查见 `backend/tests/test_access_licenses.py`，不要对生产账号运行测试。

分享安装包时推荐使用固定下载页 `https://teacher.dgplus.vip/download`。无需登录，下载按钮通过 `/api/client/version` 获取当前发布地址；登录页、教师工作台和管理员后台均有入口。页面展示版本、三步安装说明、手机使用、更新及卸载方法。后续更新先上传并校验新安装包，再修改本项目 `deploy/.env` 中的 `KANBAN_CLIENT_DOWNLOAD_URL`，最后运行 `sudo classglow update`。旧版安装包保留用于回退，不清理其他目录。

### 下载页更新日志与流量限制

下载页的“更新日志”默认展开当前可下载版本，旧版可展开查看；列表最高 560px，长内容在内部滚动。发布说明维护在 `frontend/src/releases.ts`，按新到旧排列，包含版本、日期和实际交付内容。发布新客户端时同时更新该文件和服务端版本信息；只调整下载页面不必重新打包 Windows。

106 的共享 Nginx 模式默认对本域名全部 `/downloads/` 安装包启用以下限制。旧版本直链、增加查询参数以及断点续传同样计数，登录、教学 API、备份下载和其他站点不使用这些下载限制。

| 设置项（`deploy/.env`） | 默认值 | 含义 |
| --- | --- | --- |
| `KANBAN_DOWNLOAD_RATE_KIB` | `1024` | 每个下载请求约 1 MiB/s，从开始就限速 |
| `KANBAN_DOWNLOAD_CONNECTIONS_PER_IP` | `2` | 同一来源 IP 最多同时下载 2 个文件 |
| `KANBAN_DOWNLOAD_CONNECTIONS_TOTAL` | `8` | 本站最多同时下载 8 个文件 |
| `KANBAN_DOWNLOAD_REQUESTS_PER_MINUTE` | `6` | 同一 IP 平均每分钟 6 次请求，短时最多连续 4 次 |
| `KANBAN_DOWNLOAD_REQUESTS_TOTAL_PER_MINUTE` | `20` | 本站平均每分钟 20 次请求，短时最多连续 9 次 |

请求频率采用 Nginx 漏桶算法，不是整分钟重置。超限返回 **HTTP 429**、`Retry-After: 60` 和中文重试页面，不把错误页作为 EXE 附件下载。支持单段 Range 断点续传；多段 Range 返回 416，建议使用浏览器直接下载。学校共用出口 IP 时会共享额度，可错峰下载或由管理员按实际人数调整；配置项只接受正整数。

修改 `deploy/.env` 后执行 `sudo classglow update`，脚本会渲染配置、通过 `nginx -t` 后重载。共享内存中的计数跨 Nginx worker 生效；配置校验失败会恢复原站点文件。原理见 Nginx 官方[请求频率限制](https://nginx.org/en/docs/http/ngx_http_limit_req_module.html)、[并发限制](https://nginx.org/en/docs/http/ngx_http_limit_conn_module.html)及[传输限速](https://nginx.org/en/docs/http/ngx_http_core_module.html#limit_rate)。

这些限制减少重复下载与并发刷流量，**不是每日流量配额或 DDoS 防护**。持续分布式访问仍可能产生流量；严格控制月度账单需要在云厂商配置带宽/流量预算或接入带防护的 CDN。当前按直接连接 Nginx 的来源 IP 计数，不信任客户端自报的 `X-Forwarded-For`；未来接 CDN 时，先在网关仅信任该 CDN 的官方地址段并限制源站直连，再调整真实 IP 配置。外部存储下载链接、独立 Caddy 模式需要在相应下载网关另行配置，不能仅靠本项保护。


## 0.4.12 更新准备与运行约束

本版无数据库结构变化，继续使用迁移 `a913c07b5e2f` 和原 SQLite 数据卷。新增鉴权 SSE：`/api/device/events`、`/api/classes/{class_id}/device-events`，不在 URL 中传凭证；20 秒保活，长连接不占用数据库写锁。现有 Caddy 转发事件流，本站 Nginx `location /` 关闭响应缓冲，沿用 60 秒读超时。标准部署仍为单 API worker；将来改多 worker/多实例须接共享通知总线，不能直接扩容内存通知器。断线继续每 10 秒同步。

会话默认 `KANBAN_SESSION_HOURS=720`（30 天），若自行配置较短值须同步调整；现存短期会话不会被自动延长，教师重新登录取得新有效期。请将 API、Web 和 Windows 一并升级，旧 Windows 仍可按原频率同步，但没有醒目提醒能力。停止提醒和手动同步不被高级许可到期阻挡，其他权限检查保留。

安装包：`ClassGlow-0.4.12-win-x64-setup.exe`，53,725,254 字节，SHA-256：`8af991dc4b22a8650c21898305f0c400ac5168ce99d5e479f81760dfb016187e`。上传至本项目暂存目录，核对尺寸和哈希后移入 `.deploy/downloads`；先准备安装包及下载地址，再推 main，避免自动更新定时器提前切换下载版本。服务器核验记录如下。


### 2026-11-04 22:12 线上核验

功能提交 `e9d5ae80b7481e06df43ae8be423f412811e58a0` 已通过本机 SSH 推送 GitHub main，经 Netcatty 在 106 执行 `sudo classglow update`，退出码 0。更新前一致性备份：`backup-before-deploy-20261104T141025Z-52de8fa1.db`，位于原数据卷 `/app/data/backups`。后续文档提交与自动更新可能产生新的备份，以管理后台列表为准。

API healthy、Web 正常、`nginx -t` 通过；`.deploy/deployed-commit` 指向上述功能提交。配置权限仍为 root 0600，安装包为 root 0644，专用上传暂存目录已移除。更新前后均为 2 个账号、1 个班级、5 台设备、3 条教学内容，未重置数据或绑定；原迁移号 `a913c07b5e2f` 保持不变。运行中的 API 确认会话时长为 720 小时，本站 Nginx 已启用 `proxy_buffering off`。

公网健康正常，客户端版本接口返回 0.4.12 及对应 EXE；下载页返回 200，加载的新资源包含 0.4.12 更新日志。安装包 Range 返回 `206 bytes 0-1023/53725254`，首 1024 字节与本地包一致，服务器全文件哈希与上文一致。未登录访问新增设备事件接口返回 401。通知的有效凭据推送延迟已在隔离环境验证；未伪造生产账号或生产设备来测试长连接。
