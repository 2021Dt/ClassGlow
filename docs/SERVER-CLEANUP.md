# 106 服务器停用与清理

当前服务：`teacher.dgplus.vip`，项目目录 `/opt/classglow`，沿用服务器原有 Docker、Nginx 和 Certbot。本文是管理员执行清理时的操作说明；开发部署不会自动执行这些清理步骤。

部署结构、备份恢复和运维命令见 [部署文档](DEPLOYMENT.md)。记录日期为 2026-11-04；执行清理前应以当前主机实际配置核对。只做空间维护时使用下一节，不必下线整个网站。

0.3.0 的账号、期限和许可记录均在业务数据库中，没有额外许可服务或清理定时器。终止许可不等于删除账号或教学数据；后台可撤销许可，最终删除业务数据仍按本文的数据卷与备份范围处理。

0.4.0 的自由画布、文字和压缩图片继续保存在同一业务数据库及备份中，没有新增对象存储、上传目录或宿主机服务。布局审计只记录图片指纹，避免每次拖动重复保存图片。Windows 本机缓存仍在原应用专属目录，正常卸载一并清理；未发布设计草稿仅在对应浏览器中，服务器卸载不会跨设备删除这些本机草稿。

0.4.6 新增 `device_displays` 表，每台设备最多一张压缩看板图（400 KB），位于原数据库内，没有新增图片目录或后台服务。解绑、归档、联网卸载以及恢复/迁移后会清除当前回传；历史数据库备份中的图片仍随备份保留。管理员可在“数据备份 → 详情”核对数量，再逐份删除备份；删除不可恢复，不影响正在使用的教学数据，不自动清空全部历史备份。

下载限流使用本项目 `.deploy/nginx.conf` 内的 Nginx 共享内存区域，重载/卸载沿用原站点配置流程；没有新增数据库表、宿主机服务、计划任务或下载追踪日志。更新日志位于源码 `frontend/src/releases.ts`，超限提示页位于 `frontend/public/download-busy.html`，均随项目代码维护。撤下本项目 Nginx 链接并重载后，旧 worker 退出即释放对应计数内存；不需要清理 Nginx 全局缓存或其他站点日志。

0.4.7 启动兼容更新仅增加同一下载目录内的新版安装包；上传暂存目录在校验和移动完成后移除。Windows 的新增诊断字段仍写入原 `startup.log`，沿用日志轮转与卸载清单。没有新增服务器诊断收集服务、数据库表或清理任务。

0.4.8 的公告正文、目标账号与弹出记录保存在原 SQLite 数据卷的 `announcements`、`announcement_targets`、`announcement_receipts` 表。后台撤回公告会停止后续领取，并保留发布与领取记录供核对。登录记录在下次成功密码认证时清理 30 天前的记录，恢复码只保存摘要。上述记录均进入数据库备份，彻底移除时按本文件的数据卷与备份范围清理。新增卡片图片与图形继续随布局存储。安装包位于原 `.deploy/downloads/ClassGlow-0.4.8-win-x64-setup.exe`，上传临时目录 `.deploy/incoming-048` 在文件校验与安装后移除。

## 菜单卸载

0.4.10 的玻璃底图只在 Windows 本机生成，服务器不增加渲染服务、浏览器或端口，也不新增数据库表。发布包使用 `.deploy/downloads/ClassGlow-0.4.10-win-x64-setup.exe`，上传暂存目录 `.deploy/incoming-0410` 校验并安装文件后移除。客户端的 `Renderer` 文件、`glass-material.png`、`glass-material.key` 与专用 `material-webview` 缓存由本机卸载器清理，系统共享 WebView2 不删除，详见 [Windows 隐私与卸载](WINDOWS-PRIVACY.md)。

0.4.9 的发布记录删除会移除该条内容及其修改历史，仅保留不含正文的删除审计；旧备份中的内容不会随之消失。需要清除备份副本时，在管理员“数据备份”核对后逐份删除。本版没有新增服务器服务、定时器、数据库表或上传目录；安装包位于 `.deploy/downloads/ClassGlow-0.4.9-win-x64-setup.exe`，上传暂存目录 `.deploy/incoming-049` 在校验与移动后移除。网页第三方许可是随前端构建分发的静态文本，随项目一并移除。

优先使用 `sudo classglow` 的“卸载本项目”，或直接 `sudo classglow uninstall`。默认选项会撤下本项目服务、自动更新、命令入口和 Nginx 链接，保留项目目录和数据；恢复时重新运行安装命令即可。彻底删除选项会删除 `/opt/classglow`（包括其中另放的文件）与项目数据卷，必须先离机备份并输入指定确认文字。脚本先核对固定目录、站点链接、命令入口和服务归属，遇到不一致会停止。

迁移到 PostgreSQL 后，还包括 `classroom-board_postgres_data` 卷、`.deploy/backups/*.dump` 和保留的 SQLite 配置。脚本不运行全局 Docker prune，不移除共享基础镜像。宿主机 TLS 证书与 Certbot 保留；确认域名完全弃用后按下文通过 Certbot 删除指定证书。

## 日常空间维护

先查看占用及本项目容器：

```bash
sudo du -sh /opt/classglow/.deploy/downloads /opt/classglow/backups 2>/dev/null
sudo docker ps -a --filter label=com.docker.compose.project=classroom-board
sudo classglow status
```

主数据库和卷内备份不包含在上述目录大小中。定义 [部署文档](DEPLOYMENT.md) 第 2 节的 `dc` 函数后，可用 `dc exec -T api du -sh /app/data` 查看；不要直接编辑 Docker 自己的数据目录。

| 可增长内容 | 维护方式 |
| --- | --- |
| SQLite 历史备份 | 先按学校保留期限挑选、离机备份并验证，再逐个删除确定过期的文件；不要按通配符清空 `/app/data`，也不要碰正在使用的数据库、WAL 或 SHM 文件 |
| 旧 Windows 安装包 | 核对 `/api/client/version`、`deploy/.env` 和仍在使用的下载链接，先保留当前发布包及必要回退包，再删除选定的旧文件 |
| 容器日志 | Compose 已为本项目配置 `json-file` 轮转，每份 10 MB、保留 3 份；不手工截断 Docker 正在写入的日志 |
| 自动更新日志 | 由宿主机 journald 管理；查看 `sudo classglow auto-status`，不为本项目清空全机 journal |
| 旧构建镜像/缓存 | 先核对容器引用和项目归属，只处理确定不再使用的本项目独占镜像；共享服务器不使用全局 prune |

目前没有按日期自动删除备份或下载包的任务。上传用的 `.deploy/incoming` 临时目录已在本次发布校验完成后移除；以后若出现中断上传的文件，先确认无传输任务再处理具体文件。

## 先停止自动更新并保留数据

以下是需要人工停用时的步骤，使用菜单卸载时由脚本执行。先运行 `sudo classglow auto-off`。它停止后续定时触发，已开始的部署仍会完成；通过 `sudo classglow auto-status` 查看定时任务和日志，再用以下命令检查手工/自动更新共用的锁。返回非零时先等待当前部署完成，不强行终止迁移：

```bash
sudo flock -n /opt/classglow/.deploy/update.lock true
```

检查只代表检查当时没有部署持锁；整个维护窗口还需协调其他运维人员暂停手工更新。停用后不要再执行 `classglow update` 或 `auto-on`，否则会重新部署。

执行 `sudo classglow backup`，记录输出的备份文件名。备份默认在应用数据卷内，因此删除数据卷前必须把选定备份复制到服务器外的受控位置，并验证可恢复。不要在聊天、公开仓库或网站目录保存数据库和 `deploy/.env`。

仅停止本项目且保留数据：

```bash
cd /opt/classglow
# 先按部署文档定义 dc，自动选择 Nginx 和当前数据库配置。
dc down
```

不加 `-v`；命名卷 `classroom-board_app_data` 和其中的 SQLite、备份均保留。不要停止整个 Docker 服务，也不要运行全局 `docker system prune`、`docker volume prune`。

## 撤下网站和更新入口

先核对 `/etc/nginx/sites-enabled/teacher.dgplus.vip` 指向 `/etc/nginx/sites-available/teacher.dgplus.vip`，后者指向 `/opt/classglow/.deploy/nginx.conf`；如果不符，先检查当前运维调整，不能直接删除。

确认后仅删除上述两个本项目链接，执行 `sudo nginx -t`；通过后才 `sudo systemctl reload nginx`。其他站点、Nginx 主配置和默认站点保留。

需要彻底撤销自动更新时，在定时器停止且部署结束后，删除本项目的 `/etc/systemd/system/classglow-update.timer`、`classglow-update.service` 和 `/usr/local/bin/classglow`，再运行 `sudo systemctl daemon-reload`。不要清理其他 systemd 服务或系统日志。

## 最终删除需核对的范围

| 内容 | 处理边界 |
| --- | --- |
| 数据卷 `classroom-board_app_data` | 含业务数据库、初始化密钥和备份；离机备份确认后，明确决定不保留数据时，才删除这个精确名称的卷 |
| 数据卷 `classroom-board_postgres_data` | 迁移后使用的 PostgreSQL 数据；必须先离机保存并验证对应 dump，不能当作缓存删除 |
| `/opt/classglow` | 源码、服务器配置、部署状态、ACME 验证目录及 Windows 下载包；核对真实路径、挂载点和另放文件后再决定归档或删除 |
| 项目 API/Web 镜像 | 先核对镜像 ID、容器引用和标签，只处理本项目独占的构建镜像；共享基础镜像保留 |
| Certbot `teacher.dgplus.vip` 证书 | 先撤下所有引用它的站点，确认域名不再使用后，通过 `sudo certbot delete --cert-name teacher.dgplus.vip` 管理；不要手工删整个 letsencrypt 目录 |
| 下载过的旧 ZIP/安装包 | 服务器下载目录随项目归档；老师电脑上的下载文件由用户管理 |

保留服务器原有 Vaultwarden、lingua-reader-api、mihomo、rustdesk 以及各自数据、网络和端口配置。保留 Docker、Nginx、Certbot 和它们原有的系统服务/定时器。Windows 客户端应在服务下线前完成在线卸载；下线后只能离线清理本机，原设备权限需在服务器销毁或重新上线后处理。

## 清理完成后核对

保留数据停用时，确认本项目容器已经停止，`classroom-board_app_data` 卷仍在，离机备份可读。彻底移除时，再核对本项目容器/网络、所选数据卷、站点链接、更新服务和目录是否按计划处理；另放文件或保留的归档应记录其位置。

执行 `sudo nginx -t`，确认 Nginx 和其他业务仍正常运行；核对 `classglow-update.timer` 已停用。证书只通过 Certbot 管理，确认未影响其他域名的续期。不要通过删除全局 Docker、Nginx、Certbot 配置来完成本项目清理。

本项目不承诺删除操作系统自身维护的审计、日志或缓存，不为“无痕”破坏系统记录。
