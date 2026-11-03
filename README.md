# 教室桌面看板

根据《教室桌面看板产品设计文档_v1.3》实现的 Vue 3 管理端、FastAPI 服务和 C# Windows 客户端。关键业务规则使用中文注释，后端重复验证权限、修订、展示时间与设备绑定；失败时保留草稿或最近完整画面。

当前已实现教师管理、后端和 Windows 客户端的完整业务链路，按本轮要求先使用 SQLite，并保留后续 PostgreSQL 迁移能力。尚未通过真实教室的连续运行与重启验收。详细设计见 [系统与交互设计](docs/DESIGN.md)，实际验证范围见 [验收记录](docs/VERIFICATION.md)。

**部署到服务器和手机访问：**正式域名配置为 `teacher.dgplus.vip`，代码位于 [ClassGlow](https://github.com/2021Dt/ClassGlow)。手机与电脑使用同一个 Web 管理端，教室使用 Windows 客户端；没有 Android APK。操作流程与界面见 [使用说明](docs/USAGE.md)。

Ubuntu / Debian 独立服务器，域名已解析且 80/443 可用时，一条命令安装环境、拉取 `main`、构建前后端、备份并迁移 SQLite、启动 HTTPS：

```bash
curl -fsSL https://raw.githubusercontent.com/2021Dt/ClassGlow/main/deploy/install.sh -o classglow-install.sh && sudo bash classglow-install.sh
```

之后运行 `sudo classglow update` 更新；运行 `sudo classglow auto-on` 开启每 5 分钟检查新提交并自动部署。详细要求、运维命令和恢复方式见 [服务器部署与手机使用](docs/DEPLOYMENT.md)。Windows 客户端服务地址填写 `https://teacher.dgplus.vip/api/`。

**已有 Nginx / Docker 的共享服务器：**使用 `sudo bash classglow-install.sh --existing-nginx`，只增加本项目的站点，网页容器监听 `127.0.0.1:18080`，沿用宿主机 Certbot。此模式要求现有 Docker Compose ≥ 2.24.4、Nginx、Certbot 及已注册的证书账户，不安装系统软件包。Docker 只需要在服务器运行，本地电脑不需要。

## 启动管理端与服务

需要 Python 3.13、uv、Node.js 22.18 或更新版本。以下命令在项目根目录执行：

```powershell
uv sync --group dev
Copy-Item .env.example .env
uv run python main.py
```

另开终端启动前端：

```powershell
cd frontend
npm ci
npm run dev
```

打开 [教师管理端](http://127.0.0.1:5173/)；API 文档位于 [本地接口文档](http://127.0.0.1:8000/docs)。首次打开会进入服务器初始化向导，先执行 `uv run python -m scripts.setup_key` 读取安装密钥，再填写站点名称和首个系统管理员账号。没有预置管理员或默认口令。

初始化后入口锁定。系统管理员从左侧“系统管理”（手机“我的 → 系统管理”）进入后台，可创建或停用账号、重置口令、管理全部班级和设备、设置公开注册、查看系统审计，以及备份、下载和恢复 SQLite 数据库。默认关闭公开注册，由管理员创建教师账号；可以在站点设置中开放。升级已有数据库时可填写已有账号及其原口令，将其设为首个系统管理员，已有班级和正文保留。

当前默认使用 `data/kanban.db`，开发启动时自动运行 Alembic 迁移；SQLAlchemy 数据层、事务规则和迁移脚本均保留数据库切换能力。SQLite 写请求使用事务锁，并显式启用外键检查。`.env`、数据库、浏览器测试截图和本机工具目录均已忽略，不提交凭证。

本地若需要自动重载，使用 `uv run uvicorn backend.app:app --reload --reload-dir backend --no-access-log`。原始访问日志默认关闭，避免短期邀请秘密出现在请求路径日志中；操作审计保存在数据库，错误使用请求编号定位。

## 后续迁移 PostgreSQL

1. 在 `.env` 中将 `POSTGRES_PASSWORD` 换成长随机口令，执行 `docker compose up -d db`。
2. 新建空库时，将 `KANBAN_DATABASE_URL` 配置为 `postgresql+psycopg://kanban:经过URL编码的口令@127.0.0.1:55432/kanban`；同时配置公开 HTTPS 地址和准确来源，再将 `KANBAN_ENVIRONMENT` 改为 `production`。
3. 首次启动前运行 `uv run alembic upgrade head`；生产不自动建表。
4. 配置公开 HTTPS 域名、`KANBAN_PUBLIC_URL` 与准确的 `KANBAN_ALLOWED_ORIGINS`。教师和设备均连接该服务，不要求同一局域网。前端构建后，通过反向代理将 `/api/` 转发给后端。

服务暂按单进程设计，数据库仍使用事务和行锁。提供的代理配置处理 HTTPS 和请求体上限，应用处理登录、配对限速及敏感字段长度；切换为多进程前须增加共享限流。会话与设备秘密只保存摘要；教室端不保存教师口令。

已有 SQLite 数据时，不要先在目标库建表。先停服并备份，通过环境变量设置 `KANBAN_MIGRATION_TARGET_URL` 为一个空 PostgreSQL 库，执行 `uv run python -m scripts.migrate_database`。

工具会在目标库运行迁移，按外键顺序分批搬迁业务数据并逐表核对条数，随后更换同步世代并撤销旧会话、设备、配对和待执行命令。目标非空时立即拒绝，来源不会被修改。最后切换连接配置、重新登录并绑定设备。已通过 SQLite 到隔离 SQLite 的迁移演练；真正切换 PostgreSQL 前仍需在目标环境再演练一次。

## 编译与运行 Windows 客户端

开发使用 .NET 10 SDK，目标为 `net10.0-windows`。本次在 `.tools/dotnet` 内安装了独立 SDK；其他机器可以使用自己的受支持 SDK。

```powershell
dotnet build desktop/ClassroomBoard/ClassroomBoard.csproj
dotnet run --project desktop/ClassroomBoard/ClassroomBoard.csproj
```

已有本机独立 SDK 时，将命令中的 `dotnet` 换成 `& .\.tools\dotnet\dotnet.exe`。已绑定客户端启动后只有托盘入口；首次配置页支持服务地址、短期绑定码、二维码、当前用户开机启动和本机启用展示。

程序默认关闭壁纸写入，先完成配对与同步检查，再在本机启用。当前无打扰策略采用保守条件：只有前台本来就是桌面时才尝试应用；课件、视频与输入期间继续缓存并报告延后。不会主动切换桌面、激活课件、注入按键或重启 Explorer。这一策略仍须在真实教室连续运行验证。

壁纸、暂停恢复和现场控制均作用于本机指定目标。原壁纸、加密凭证、缓存及命令处理结果保存在当前用户的 `%LOCALAPPDATA%/ClassroomBoard`，使用 DPAPI 加密与原子文件替换。正常退出恢复原壁纸；异常终止时旧壁纸可能保留。首次安装必须验证学校还原保护和重启后的持久化，不能假设 D 盘一定安全。

创建可复制运行包：

```powershell
dotnet publish desktop/ClassroomBoard/ClassroomBoard.csproj -c Release -r win-x64 --self-contained true -o dist/windows
```

当前没有自动升级和签名发布。首次上线需由管理员人工安装，保留原数据目录。

本次已生成 [Windows x64 开发测试包](dist/班级看板-开发测试版-win-x64.zip)，解压后运行 `ClassroomBoard.exe`，不需要额外安装 .NET 运行时。它是免安装、未签名的测试包，使用说明随包提供。服务端仍需按上方命令启动；客户端默认不更改壁纸。

## 已实现的关键行为

- 首次安装向导与一次性初始化、系统管理员后台、全局账号管理、班级所有者调整、全局设备撤销、注册设置、运行概况、系统审计，以及 SQLite 网页备份恢复。
- 账号登录、可撤销会话、多班与学科权限、邀请申请审批、成员权限维护、所有权转移、归档与恢复接口。
- 私人云草稿、停顿保存、本机未同步副本、最终输入保存、草稿与正式记录修订校验、多班原子发布与幂等结果查询。
- 作业、提醒、完成与恢复、撤回、代改原因与修改历史；时间使用北京时间解释，存储带时区时间。
- 浅灰 / 深灰云母、通透与纯色材质、上传背景图片或选择底色、卡片不透明度、字号与图标留白。Windows 采用一张大卡片容纳展示区，作业 / 提醒 / 课表支持拖动、右下角拉伸、显隐、精确尺寸、三种布局预设和左右镜像；标题与底部更新栏固定。布局和背景通过正式快照一起同步。
- 可视化周课表、作息、只读查询、未来生效调整与旧区间保留、指定日期停课或逐节改课、寄语、倒计时、高优先级提醒上限。
- 短期绑定码与二维码、只读设备凭证、正式快照、条件拉取、缓存与应用版本、控制修订、命令分组、过期与回执；未知结果保留原请求标识，可查询或幂等重试。
- C# 托盘、DPAPI 状态、原子缓存、按时间过滤、文字分页、主题与材质回退、保守应用、现场暂停优先、壁纸恢复。

## 自动化验证

```powershell
uv run pytest -q
uv run ruff check backend scripts
cd frontend
npm test
npm run build
```

C# 安全排版自检只生成文件，不更换壁纸：

```powershell
dotnet run --project desktop/ClassroomBoard/ClassroomBoard.csproj -- --self-test --output .qa/desktop
```

查看真实 Windows 材质与可编辑布局：先启动前端开发服务，再打开 `/preview.html`。它使用独立示例数据，不需要初始化生产账号。生成 Windows 固定样例：

```powershell
dotnet run --project desktop/ClassroomBoard/ClassroomBoard.csproj -- --render-preview --snapshot frontend/dev/board-snapshot.json --output frontend/public/review
```

“Windows 实际渲染”展示导出的固定样例；取消勾选可上传图片、调整布局并查看 Web 实时预览。此命令只输出图片，不设置当前电脑壁纸。预览入口只在开发服务中使用。

在独立测试环境完成初始化并开放注册、启动后端及编译客户端后，可以运行 `uv run python scripts/client_integration.py`。该脚本使用临时测试账号和明确标记的测试班级，启动真实 C# 客户端验证绑定、快照缓存、同步回执与暂停延后，强制关闭壁纸写入。结果保存在 `.qa`；不要把该结果当作真实桌面应用验收。

## 备份与恢复

当前 SQLite 可在线备份，恢复总是写入新文件：

```powershell
uv run python -m scripts.sqlite_backup backup
uv run python -m scripts.sqlite_backup restore --source data/backups/需要恢复的备份.db
```

备份使用 SQLite 原生一致性备份 API，并检查数据库完整性与外键。恢复后更换同步世代、撤销旧授权，停止服务并将连接配置指向恢复文件，再重新登录和绑定设备。不会覆盖正在使用的数据库。

使用 PostgreSQL 的 `pg_dump` 及 `scripts/backup.ps1`，通过 `PGPASSFILE` 配置口令，每日备份并保留 14 天。恢复须停服，先在隔离数据库还原，再运行 `uv run python -m scripts.rotate_epoch` 更换同步世代并撤销旧会话、设备与命令，然后开放服务。教师重新登录、设备重新绑定。试点前必须做一次真正的备份还原演练。
