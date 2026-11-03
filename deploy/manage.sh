#!/usr/bin/env bash
# 单机运维入口：拉取、构建、备份、迁移、启动；数据库始终保存在命名卷中。
set -Eeuo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
dc() { docker compose --env-file deploy/.env -f compose.production.yaml "$@"; }
log() { printf '\n[ClassGlow] %s\n' "$*"; }

update() {
  local mode="${1:-manual}" target deployed
  mkdir -p .deploy
  # 手工更新与定时更新共用同一把锁，避免同时迁移数据库。
  exec 9>.deploy/update.lock
  if ! flock -n 9; then
    log '已有部署正在运行，本次跳过。'
    return
  fi
  if [[ "$mode" == --auto && -f .deploy/failed ]]; then
    log '上次部署失败，自动更新已暂停；修复后执行 classglow update。'
    return
  fi
  if [[ -n "$(git status --porcelain)" ]]; then
    echo '服务器工作区有未提交修改，请先处理；部署配置应写入 deploy/.env。' >&2
    return 1
  fi
  if [[ "$(git branch --show-current)" != main ]]; then
    echo '部署工作区须使用 main 分支。' >&2
    return 1
  fi
  export GIT_TERMINAL_PROMPT=0
  export GIT_SSH_COMMAND="${GIT_SSH_COMMAND:-ssh -o BatchMode=yes -o ConnectTimeout=15}"
  log '拉取 main 分支并快进整合代码'
  git fetch --prune origin main
  # 不生成自动合并提交，不覆盖服务器的本地修改。
  if ! git merge-base --is-ancestor HEAD origin/main; then
    echo '服务器有 main 之外的本地提交或分支分歧，请处理后再部署。' >&2
    return 1
  fi
  git merge --ff-only origin/main
  target="$(git rev-parse HEAD)"
  deployed="$(cat .deploy/deployed-commit 2>/dev/null || true)"
  if [[ "$mode" == --auto && "$target" == "$deployed" ]]; then
    log '没有新的提交。'
    return
  fi
  # 拉取后执行本次提交里的部署逻辑，锁由父进程持有直到子进程退出。
  CLASSGLOW_LOCK_HELD=1 bash "$ROOT/deploy/manage.sh" _deploy "$target"
}

deploy_current() {
  local revision="$1" domain phase='检查配置'
  if [[ "${CLASSGLOW_LOCK_HELD:-}" != 1 ]]; then
    echo '请通过 classglow update 部署。' >&2
    return 1
  fi
  # 迁移失败时不自动回滚数据库，避免覆盖停服前已经提交的教学内容。
  trap 'printf "%s\n" "$phase" > .deploy/failed; echo "部署失败：$phase。请查看 classglow logs，处理后重新执行 classglow update。" >&2; exit 1' ERR INT TERM
  domain="$(sed -n 's/^KANBAN_DOMAIN=//p' deploy/.env | tr -d '\r')"
  if [[ ! "$domain" =~ ^[a-zA-Z0-9][a-zA-Z0-9.-]*\.[a-zA-Z]{2,}$ ]]; then
    echo 'deploy/.env 中 KANBAN_DOMAIN 请填写不带引号的完整域名。' >&2
    false
  fi
  dc config --quiet
  phase='构建前后端镜像（此阶段原服务继续运行）'
  log "$phase"
  dc build --pull api web
  phase='停服并备份数据库'
  log "$phase"
  dc stop web api
  dc run --rm --no-deps api python -m scripts.deploy_backup
  phase='数据库迁移'
  log "$phase"
  dc run --rm --no-deps migrate
  phase='启动 API 与网页'
  log "$phase"
  # 迁移已同步完成；分别等待 API 健康和网页启动，避免重复运行 migrate 容器。
  dc up -d --no-deps --wait --wait-timeout 120 api
  dc up -d --no-deps --wait --wait-timeout 120 web
  phase='HTTPS 与接口检查'
  log "$phase"
  # 直接连接本机网关并核对正式域名证书；证书签发仍要求公网 DNS 和 80/443 可达。
  curl --fail --silent --show-error --retry 12 --retry-delay 5 --retry-all-errors \
    --retry-max-time 120 --connect-timeout 5 --max-time 10 \
    --resolve "$domain:443:127.0.0.1" "https://$domain/api/health"
  curl --fail --silent --show-error --connect-timeout 5 --max-time 10 \
    --resolve "$domain:443:127.0.0.1" "https://$domain/" -o /dev/null
  printf '%s\n' "$revision" > .deploy/deployed-commit
  rm -f .deploy/failed
  trap - ERR INT TERM
  dc ps
  log "部署完成：$revision；访问 https://$domain"
}

auto_on() {
  if [[ "$ROOT" != /opt/classglow ]]; then
    echo '定时服务使用 /opt/classglow，请先运行 deploy/install.sh。' >&2
    return 1
  fi
  cat > /etc/systemd/system/classglow-update.service <<'EOF'
[Unit]
Description=ClassGlow pull, build and deploy
Wants=network-online.target
After=network-online.target docker.service
Requires=docker.service

[Service]
Type=oneshot
WorkingDirectory=/opt/classglow
ExecStart=/usr/local/bin/classglow update --auto
TimeoutStartSec=30min
UMask=0077
EOF
  cat > /etc/systemd/system/classglow-update.timer <<'EOF'
[Unit]
Description=Check ClassGlow main branch every five minutes

[Timer]
OnBootSec=2min
OnUnitInactiveSec=5min
Unit=classglow-update.service

[Install]
WantedBy=timers.target
EOF
  systemctl daemon-reload
  systemctl enable --now classglow-update.timer
  log '已启用：每 5 分钟检查 main，新提交自动构建部署；更新期间会短暂停服。'
}

main() {
  if [[ "${1:-help}" == help || "${1:-}" == --help ]]; then
    echo '用法：classglow {update|status|logs|setup-key|backup|auto-on|auto-off|auto-status}'
    return
  fi
  if (( EUID != 0 )); then
    echo '请使用 sudo classglow 执行运维操作。' >&2
    exit 1
  fi
  case "${1:-}" in
    update) update "${2:-manual}" ;;
    _deploy) deploy_current "$2" ;;
    status) dc ps -a; if [[ -f .deploy/deployed-commit ]]; then cat .deploy/deployed-commit; fi ;;
    logs) dc logs --tail=100 api web ;;
    setup-key) dc exec -T api python -m scripts.setup_key ;;
    backup) dc exec -T api python -m scripts.deploy_backup ;;
    auto-on) auto_on ;;
    auto-off) systemctl disable --now classglow-update.timer; log '已停止后续定时检查；已开始的部署会完成。' ;;
    auto-status) systemctl list-timers classglow-update.timer; journalctl -u classglow-update.service -n 40 --no-pager ;;
    *) echo '未知操作，运行 classglow help 查看命令。' >&2; exit 1 ;;
  esac
}

main "$@"
