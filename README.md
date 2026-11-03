# 教室桌面看板

根据《教室桌面看板产品设计文档_v1.3》实现的 Vue 3 管理端、FastAPI 服务和 C# Windows 客户端。关键业务规则使用中文注释，后端重复验证权限、修订、展示时间与设备绑定；失败时保留草稿或最近完整画面。

当前已实现教师管理、后端和 Windows 客户端的完整业务链路，按本轮要求先使用 SQLite，并保留后续 PostgreSQL 迁移能力。尚未通过真实教室的连续运行与重启验收。详细设计见 [系统与交互设计](docs/DESIGN.md)，实际验证范围见 [验收记录](docs/VERIFICATION.md)。

**部署到服务器和手机访问：**正式域名配置为 `teacher.dgplus.vip`，代码位于 [ClassGlow](https://github.com/2021Dt/ClassGlow)。手机与电脑使用同一个 Web 管理端，教室使用 Windows 客户端；没有 Android APK。操作流程与界面见 [使用说明](docs/USAGE.md)。

Ubuntu / Debian 独立服务器，域名已解析且 80/443 可用时，一条命令安装环境、拉取 `main`、构建前后端、备份并迁移 SQLite、启动 HTTPS：

```bash
curl -fsSL https://raw.githubusercontent.com/2021Dt/ClassGlow/main/deploy/install.sh -o classglow-install.sh && sudo bash classglow-install.sh
```

安装后运行 `sudo classglow` 打开交互菜单：状态、更新、管理员初始化/重置、备份、SQLite → PostgreSQL 迁移、自动更新和卸载。仍可用 `sudo classglow update` 直接更新；`sudo classglow auto-on` 开启每 5 分钟自动检查新提交。详细要求、运维命令和恢复方式见 [服务器部署与手机使用](docs/DEPLOYMENT.md)。Windows 客户端服务地址填写 `https://teacher.dgplus.vip/api/`。

**已有 Nginx / Docker 的共享服务器：**使用 `sudo bash classglow-install.sh --existing-nginx`，只增加本项目的站点，网页容器监听 `127.0.0.1:18080`，沿用宿主机 Certbot。此模式要求现有 Docker Compose ≥ 2.24.4、Nginx、Certbot 及已注册的证书账户，不安装系统软件包。Docker 只需要在服务器运行，本地电脑不需要。

106 服务器的当前部署记录、更新和备份恢复见 [部署文档](docs/DEPLOYMENT.md)；日常空间维护、停用及移除顺序见 [后期清理文档](docs/SERVER-CLEANUP.md)。

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

打开 [教师管理端](http://127.0.0.1:5173/)；API 文档位于 [本地接口文档](http://127.0.0.1:8000/docs)。教师首页只有账号、密码和登录按钮，没有注册、激活账号或服务器设置入口。本地开发首次管理员由 `uv run python -m scripts.admin_account init` 在终端创建，密码隐藏输入；生产服务器使用 `sudo classglow` 菜单中的“初始化管理员”。

管理员登录后在“系统管理 → 创建账号”设置老师初始密码、身份、账号有效期、班级和学科。老师拿到账密直接登录；高级功能的激活码在登录后的“我的账号”填写。详情见 [账号与许可说明](docs/ACCOUNT-LICENSES.md)。旧账号升级后保留原登录、建班权限和数据；普通旧账号的高级功能需领取许可。

当前默认使用 `data/kanban.db`，开发启动时自动运行 Alembic 迁移；SQLAlchemy 数据层、事务规则和迁移脚本均保留数据库切换能力。SQLite 写请求使用事务锁，并显式启用外键检查。`.env`、数据库、浏览器测试截图和本机工具目录均已忽略，不提交凭证。

本地若需要自动重载，使用 `uv run uvicorn backend.app:app --reload --reload-dir backend --no-access-log`。原始访问日志默认关闭，避免短期邀请秘密出现在请求路径日志中；操作审计保存在数据库，错误使用请求编号定位。

## 后续迁移 PostgreSQL

已部署的服务器使用 `sudo classglow` 菜单选择“迁移 SQLite → PostgreSQL”，脚本负责备份、停写、空库迁移与切换配置，保留原 SQLite；详情见部署文档。以下步骤仅用于本地开发或手工接入外部数据库：

1. 在 `.env` 中将 `POSTGRES_PASSWORD` 换成长随机口令，执行 `docker compose up -d db`。
2. 新建空库时，将 `KANBAN_DATABASE_URL` 配置为 `postgresql+psycopg://kanban:经过URL编码的口令@127.0.0.1:55432/kanban`；同时配置公开 HTTPS 地址和准确来源，再将 `KANBAN_ENVIRONMENT` 改为 `production`。
3. 首次启动前运行 `uv run alembic upgrade head`；生产不自动建表。
4. 配置公开 HTTPS 域名、`KANBAN_PUBLIC_URL` 与准确的 `KANBAN_ALLOWED_ORIGINS`。教师和设备均连接该服务，不要求同一局域网。前端构建后，通过反向代理将 `/api/` 转发给后端。

服务暂按单进程设计，数据库仍使用事务和行锁。提供的代理配置处理 HTTPS 和请求体上限，应用处理登录、配对限速及敏感字段长度；切换为多进程前须增加共享限流。会话与设备秘密只保存摘要；教室端不保存教师口令。

已有 SQLite 数据时，不要先在目标库建表。先停服并备份，通过环境变量设置 `KANBAN_MIGRATION_TARGET_URL` 为一个空 PostgreSQL 库，执行 `uv run python -m scripts.migrate_database`。

工具会在目标库运行迁移，按外键顺序分批搬迁业务数据并逐表核对条数，随后更换同步世代并撤销旧会话、设备、配对和待执行命令。目标非空时立即拒绝，来源不会被修改。最后切换连接配置、重新登录并绑定设备。已完成 SQLite 到隔离 SQLite 与 PostgreSQL 17 的迁移演练，生产仍使用 SQLite；业务并发和教室现场验收另行安排。

## 编译与运行 Windows 客户端

开发使用 .NET 10 SDK，目标为 `net10.0-windows`。本次在 `.tools/dotnet` 内安装了独立 SDK；其他机器可以使用自己的受支持 SDK。

```powershell
dotnet build desktop/ClassroomBoard/ClassroomBoard.csproj
dotnet run --project desktop/ClassroomBoard/ClassroomBoard.csproj
```

已有本机独立 SDK 时，将命令中的 `dotnet` 换成 `& .\.tools\dotnet\dotnet.exe`。已绑定客户端启动后只有托盘入口；首次配置页支持服务地址、短期绑定码、二维码、当前用户开机启动和本机启用展示。

程序默认关闭壁纸写入，先完成配对与同步检查，再在本机启用。当前无打扰策略采用保守条件：只有前台本来就是桌面时才尝试应用；课件、视频与输入期间继续缓存并报告延后。不会主动切换桌面、激活课件、注入按键或重启 Explorer。这一策略仍须在真实教室连续运行验证。

壁纸、暂停恢复和现场控制均作用于本机指定目标。原壁纸路径、凭证、业务快照及命令结果保存在当前用户的 `%LOCALAPPDATA%/ClassroomBoard/state.bin`，使用 DPAPI 加密与原子替换；渲染后的 PNG 是明文图片。正常退出恢复本程序仍占用的壁纸，保留用户后来手动更换的壁纸；异常终止时旧壁纸可能保留。首次安装必须验证学校还原保护和重启后的持久化，不能假设 D 盘一定安全。

创建可复制运行包：

```powershell
dotnet publish desktop/ClassroomBoard/ClassroomBoard.csproj -c Release -r win-x64 --self-contained true -o dist/windows
```

交付推荐使用 [Windows x64 0.4.2 安装包](https://teacher.dgplus.vip/downloads/ClassGlow-0.4.2-win-x64-setup.exe)，内置 .NET 运行时，仅安装到当前用户，无需管理员权限。首次运行分为“隐私说明 → 连接班级 → 展示设置”三页；完成后进入状态页。窗口采用 WPF UI 的圆角标题栏与云母背景；同意前不联网、不创建业务数据。Windows“已安装的应用”、开始菜单和托盘均可卸载。清理范围与离线处理见 [Windows 隐私与卸载](docs/WINDOWS-PRIVACY.md)。当前仍为未签名测试版，没有客户端自动升级。

构建安装包需要 .NET 10 SDK、Python 和 NSIS 3；脚本不会安装全局工具：

```powershell
uv run python scripts/package_windows.py --nsis 'C:\路径\NSIS\makensis.exe'
```

输出 `dist/ClassGlow-0.4.2-win-x64-setup.exe`；`--qa` 构建使用独立目录及注册表项的卸载验证包。旧版便携 ZIP 仅供开发运行，不作为正式卸载入口。

## 已实现的关键行为

- 服务器菜单与一次性管理员初始化、系统管理员后台、统一账号创建与高级许可、全局账号管理、班级所有者调整、全局设备撤销、运行概况、系统审计，以及 SQLite 网页备份恢复。
- 账号登录、可撤销会话、多班与学科权限、邀请申请审批、成员权限维护、所有权转移、归档与恢复接口。
- 私人云草稿、停顿保存、本机未同步副本、最终输入保存、草稿与正式记录修订校验、多班原子发布与幂等结果查询。
- 作业、提醒、完成与恢复、撤回、代改原因与修改历史；时间使用北京时间解释，存储带时区时间。
- 浅灰 / 深灰云母、通透与纯色材质、图片 / 纯色背景。Windows 0.4.0 支持自由画布：作业、提醒、课表、时钟、倒计时、寄语、文字、图片、色块共 9 类组件，可添加、拖动、缩放、叠放、隐藏、锁定、复制、调整图层及撤销重做，也可从模板或空白画布开始。长内容分页、组件尺寸固定；旧客户端暂用基础看板并提示升级。
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

历史联调脚本依赖旧注册入口，新版管理员开户流程请使用隔离账号验收。旧版参考命令为 `uv run python scripts/client_integration.py`。该脚本使用临时测试账号和明确标记的测试班级，启动真实 C# 客户端验证绑定、快照缓存、同步回执与暂停延后，强制关闭壁纸写入。结果保存在 `.qa`；不要把该结果当作真实桌面应用验收。

## 备份与恢复

当前 SQLite 可在线备份，恢复总是写入新文件：

```powershell
uv run python -m scripts.sqlite_backup backup
uv run python -m scripts.sqlite_backup restore --source data/backups/需要恢复的备份.db
```

备份使用 SQLite 原生一致性备份 API，并检查数据库完整性与外键。恢复后更换同步世代、撤销旧授权，停止服务并将连接配置指向恢复文件，再重新登录和绑定设备。不会覆盖正在使用的数据库。

使用 PostgreSQL 的 `pg_dump` 及 `scripts/backup.ps1`，通过 `PGPASSFILE` 配置口令，每日备份并保留 14 天。恢复须停服，先在隔离数据库还原，再运行 `uv run python -m scripts.rotate_epoch` 更换同步世代并撤销旧会话、设备与命令，然后开放服务。教师重新登录、设备重新绑定。试点前必须做一次真正的备份还原演练。
