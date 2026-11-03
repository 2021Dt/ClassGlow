# 106 服务器停用与清理

当前服务：`teacher.dgplus.vip`，项目目录 `/opt/classglow`，沿用服务器原有 Docker、Nginx 和 Certbot。本文是管理员执行清理时的操作说明；开发部署不会自动执行这些清理步骤。

## 先停止自动更新并保留数据

先运行 `sudo classglow auto-off`。它停止后续定时触发，已开始的部署仍会完成；通过 `sudo classglow auto-status` 确认没有部署正在执行，再操作容器和配置。

执行 `sudo classglow backup`，记录输出的备份文件名。备份默认在应用数据卷内，因此删除数据卷前必须把选定备份复制到服务器外的受控位置，并验证可恢复。不要在聊天、公开仓库或网站目录保存数据库和 `deploy/.env`。

仅停止本项目且保留数据：

```bash
cd /opt/classglow
sudo docker compose --env-file deploy/.env -f compose.production.yaml -f compose.nginx.yaml down
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
| `/opt/classglow` | 源码、服务器配置、部署状态、ACME 验证目录及 Windows 下载包；核对真实路径、挂载点和另放文件后再决定归档或删除 |
| 项目 API/Web 镜像 | 先核对镜像 ID、容器引用和标签，只处理本项目独占的构建镜像；共享基础镜像保留 |
| Certbot `teacher.dgplus.vip` 证书 | 先撤下所有引用它的站点，确认域名不再使用后，通过 `sudo certbot delete --cert-name teacher.dgplus.vip` 管理；不要手工删整个 letsencrypt 目录 |
| 下载过的旧 ZIP/安装包 | 服务器下载目录随项目归档；老师电脑上的下载文件由用户管理 |

保留服务器原有 Vaultwarden、lingua-reader-api、mihomo、rustdesk 以及各自数据、网络和端口配置。保留 Docker、Nginx、Certbot 和它们原有的系统服务/定时器。Windows 客户端应在服务下线前完成在线卸载；下线后只能离线清理本机，原设备权限需在服务器销毁或重新上线后处理。

本项目不承诺删除操作系统自身维护的审计、日志或缓存，不为“无痕”破坏系统记录。
