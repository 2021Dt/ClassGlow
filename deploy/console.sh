#!/usr/bin/env bash
# 由 manage.sh 加载，所有操作复用项目 Compose 和部署锁，不操作其他站点或全局 Docker 数据。

postgres_enabled() { grep -qx 'KANBAN_DATABASE=postgres' "${CLASSGLOW_ENV:-deploy/.env}"; }

server_backup() {
  if postgres_enabled; then
    local output
    install -d -m 0700 .deploy/backups
    output=".deploy/backups/postgres-$(date -u +%Y%m%dT%H%M%SZ)-$$.dump"
    (umask 077; dc exec -T db pg_dump -U kanban -d kanban -Fc > "$output")
    [[ -s "$output" ]] || { echo '数据库备份没有生成，停止后续操作。' >&2; return 1; }
    log "PostgreSQL 备份完成：$ROOT/$output"
  elif [[ "${1:-online}" == offline ]]; then
    dc run --rm --no-deps api python -m scripts.deploy_backup
  else
    dc exec -T api python -m scripts.deploy_backup
  fi
}

server_status() {
  printf '\nClassGlow · 班级看板\n'
  printf '项目：%s\n' "$ROOT"
  printf '网站：https://%s\n' "$(sed -n 's/^KANBAN_DOMAIN=//p' deploy/.env | tr -d '\r')"
  if postgres_enabled; then echo '数据库：PostgreSQL（项目私有网络）'; else echo '数据库：SQLite'; fi
  printf '自动更新：'; systemctl is-active classglow-update.timer || true
  if [[ -f .deploy/database-migration.env ]]; then echo '数据库迁移未收尾：选择“迁移数据库”继续，不要手工删除目标库。'; fi
  if [[ -f .deploy/deployed-commit ]]; then printf '当前版本：'; cut -c1-12 .deploy/deployed-commit; fi
  dc ps -a --format 'table {{.Service}}\t{{.Status}}'
}

migrate_database() (
  exec 9>.deploy/update.lock
  flock -n 9 || { echo '有更新或维护正在运行，请稍后再试。'; exit 1; }
  if postgres_enabled && [[ ! -f .deploy/database-migration.env ]]; then
    echo '当前已使用 PostgreSQL，无需迁移。'; exit 0
  fi
  echo '将本项目 SQLite 迁移到 Docker 内的 PostgreSQL 17。目标必须为空，不开放数据库端口。'
  echo '期间短暂停服；先备份，保留原 SQLite。完成后需重新登录、重新绑定教室设备。'
  read -r -p '输入 MIGRATE 确认，其他输入取消：' answer
  [[ "$answer" == MIGRATE ]] || exit 0
  python3 scripts/prepare_postgres.py
  # 更新程序遇到 pending 配置会停止，确保人工重试前不会更改源码或再次迁移。
  CLASSGLOW_ENV=.deploy/database-migration.env dc up -d --wait --wait-timeout 180 db
  if [[ "$(cat .deploy/database-migration.state 2>/dev/null || true)" != copied ]]; then
    server_backup
    dc stop web api
    if ! CLASSGLOW_ENV=.deploy/database-migration.env dc --profile database-migration run --rm --no-deps migrate-sqlite; then
      dc up -d --no-deps --wait api web
      echo '迁移未完成，SQLite 服务已恢复。源数据和备份均保留；目标未被删除。' >&2
      exit 1
    fi
    printf 'copied\n' > .deploy/database-migration.state
  fi
  install -m 0600 .deploy/database-migration.env deploy/.env
  dc up -d --no-deps --wait --wait-timeout 120 api
  dc up -d --no-deps --wait web
  # 仅在新库服务启动成功后移除进行中标记，失败时可从同一目标继续，不重复复制。
  rm -- .deploy/database-migration.env .deploy/database-migration.state
  log '迁移完成。原 SQLite 与旧配置保留。后续更新和备份已切换为 PostgreSQL。'
)

uninstall_service() (
  [[ "$ROOT" == /opt/classglow && "$(realpath "$ROOT")" == /opt/classglow ]] || {
    echo '卸载只支持固定部署目录 /opt/classglow，当前目录不匹配。'; exit 1;
  }
  exec 9>.deploy/update.lock
  flock -n 9 || { echo '有更新或维护正在运行，请稍后再试。'; exit 1; }
  local domain available enabled mode answer
  domain="$(sed -n 's/^KANBAN_DOMAIN=//p' deploy/.env | tr -d '\r')"
  [[ "$domain" =~ ^[a-zA-Z0-9][a-zA-Z0-9.-]*\.[a-zA-Z]{2,}$ ]]
  available="/etc/nginx/sites-available/$domain"; enabled="/etc/nginx/sites-enabled/$domain"
  if grep -qx 'KANBAN_GATEWAY=nginx' deploy/.env; then
    for target in "$available" "$enabled"; do
      [[ ! -e "$target" && ! -L "$target" ]] || [[ -L "$target" && "$(readlink -f "$target")" == "$ROOT/.deploy/nginx.conf" ]] || {
        echo "站点配置归属不符，停止卸载：$target"; exit 1;
      }
    done
  fi
  if [[ -e /usr/local/bin/classglow ]]; then
    grep -qF 'exec bash /opt/classglow/deploy/manage.sh "$@"' /usr/local/bin/classglow || {
      echo 'classglow 入口不是本项目生成的文件，停止卸载。'; exit 1;
    }
  fi
  for unit in /etc/systemd/system/classglow-update.service /etc/systemd/system/classglow-update.timer; do
    [[ ! -e "$unit" ]] || grep -qE 'ClassGlow|classglow-update.service' "$unit" || {
      echo "定时服务归属不符，停止卸载：$unit"; exit 1;
    }
  done
  printf '\n1) 卸载服务，保留项目目录、数据库和备份（默认）\n2) 彻底卸载本项目：同时删除项目目录、数据库、备份和下载包\n0) 取消\n'
  read -r -p '请选择 [1]：' mode; mode="${mode:-1}"
  [[ "$mode" == 1 || "$mode" == 2 ]] || exit 0
  echo '范围：ClassGlow 容器、网络、自动更新、命令入口和本域名的 Nginx 链接。'
  echo '保留其他业务、Docker、Nginx、Certbot、宿主机证书和共享构建缓存。'
  if [[ "$mode" == 2 ]]; then
    # 不进入项目内额外挂载的数据目录；即使同一文件系统的 bind mount 也不能递归删除。
    if findmnt -rn -o TARGET | grep -q '^/opt/classglow/'; then
      echo '项目内存在额外挂载目录，请先人工核对并卸载挂载点，再执行彻底卸载。'; exit 1
    fi
    echo "将永久删除 $ROOT 及项目数据卷（包含其中另放的文件）。请先将备份复制到服务器外。"
    read -r -p '输入 DELETE CLASSGLOW DATA 确认：' answer
    [[ "$answer" == 'DELETE CLASSGLOW DATA' ]] || exit 0
  else
    read -r -p '输入 UNINSTALL 确认：' answer
    [[ "$answer" == UNINSTALL ]] || exit 0
  fi
  if [[ -e /etc/systemd/system/classglow-update.timer ]]; then systemctl disable --now classglow-update.timer; fi
  if [[ "$mode" == 2 ]]; then
    dc down --volumes
    # 若迁移曾中断，目标数据库也属于同一项目；沿用保留的配置清理，不触碰其他卷。
    if [[ -f .deploy/database-migration.env ]]; then CLASSGLOW_ENV=.deploy/database-migration.env dc down --volumes; fi
  else
    dc down
    if [[ -f .deploy/database-migration.env ]]; then CLASSGLOW_ENV=.deploy/database-migration.env dc down; fi
  fi
  if grep -qx 'KANBAN_GATEWAY=nginx' deploy/.env; then
    rm -f -- "$enabled" "$available"
    if ! nginx -t; then
      ln -s "$ROOT/.deploy/nginx.conf" "$available"; ln -s "$available" "$enabled"
      echo 'Nginx 校验未通过，已还原站点链接，项目文件仍保留，请排查后重试。'; exit 1
    fi
    systemctl reload nginx
  fi
  rm -f /etc/systemd/system/classglow-update.timer /etc/systemd/system/classglow-update.service /usr/local/bin/classglow
  systemctl daemon-reload
  if [[ "$mode" == 2 ]]; then
    docker image rm classroom-board-api:local classroom-board-web:latest 2>/dev/null || true
    cd /
    rm -rf --one-file-system -- /opt/classglow
    echo '本项目已移除。共享组件与宿主机 TLS 证书保留。'
  else
    echo "服务已卸载，数据和项目目录保留在 $ROOT；恢复时运行部署安装命令。"
  fi
)

console_menu() {
  local choice
  while true; do
    if [[ -t 1 ]]; then printf '\033[2J\033[H'; fi
    server_status
    cat <<'MENU'

  ClassGlow 服务器管理
  ----------------------------------------
   1. 查看状态
   2. 拉取并部署更新
   3. 初始化管理员
   4. 重置管理员密码
   5. 立即备份数据库
   6. 查看备份位置
   7. 迁移 SQLite → PostgreSQL
   8. 开启自动更新
   9. 关闭自动更新
  10. 查看运行日志
  11. 查看更新记录
  12. 卸载本项目
   0. 退出
  ----------------------------------------
MENU
    read -r -p '请选择：' choice || return 0
    # 独立子进程保留 set -e 语义；失败只返回菜单，不误继续迁移或删除。
    case "$choice" in
      0) return 0 ;;
      1) server_status ;;
      2) bash "$ROOT/deploy/manage.sh" update || true ;;
      3) bash "$ROOT/deploy/manage.sh" admin-init || true ;;
      4) bash "$ROOT/deploy/manage.sh" admin-reset || true ;;
      5) bash "$ROOT/deploy/manage.sh" backup || true ;;
      6) bash "$ROOT/deploy/manage.sh" backup-list || true ;;
      7) bash "$ROOT/deploy/manage.sh" migrate-db || true ;;
      8) bash "$ROOT/deploy/manage.sh" auto-on || true ;;
      9) bash "$ROOT/deploy/manage.sh" auto-off || true ;;
      10) bash "$ROOT/deploy/manage.sh" logs || true ;;
      11) bash "$ROOT/deploy/manage.sh" auto-status || true ;;
      12) bash "$ROOT/deploy/manage.sh" uninstall || true; return 0 ;;
      *) echo '请输入菜单中的数字。' ;;
    esac
    read -r -p '按回车返回菜单…' choice || return 0
  done
}
