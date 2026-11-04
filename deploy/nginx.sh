#!/usr/bin/env bash
# 仅接入本项目域名，不安装系统包、不重启 Docker、不改其他 Nginx 站点。
set -Eeuo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
DOMAIN="$(sed -n 's/^KANBAN_DOMAIN=//p' deploy/.env | tr -d '\r')"
[[ "$ROOT" == /opt/classglow && "$DOMAIN" =~ ^[a-zA-Z0-9][a-zA-Z0-9.-]*\.[a-zA-Z]{2,}$ ]]

# 只读取指定配置，不 source .env。限制值须为正整数，避免错误配置关闭限流。
read_download_limit() {
  local key="$1" fallback="$2" maximum="$3" value
  value="$(sed -n "s/^${key}=//p" deploy/.env | tr -d '\r')"
  value="${value:-$fallback}"
  if [[ ! "$value" =~ ^[1-9][0-9]{0,5}$ ]] || (( value > maximum )); then
    echo "$key 必须是 1 到 $maximum 的整数。" >&2
    return 1
  fi
  printf '%s' "$value"
}
DOWNLOAD_RATE="$(read_download_limit KANBAN_DOWNLOAD_RATE_KIB 1024 102400)"
DOWNLOAD_REQUESTS="$(read_download_limit KANBAN_DOWNLOAD_REQUESTS_PER_MINUTE 6 6000)"
DOWNLOAD_REQUESTS_TOTAL="$(read_download_limit KANBAN_DOWNLOAD_REQUESTS_TOTAL_PER_MINUTE 20 60000)"
DOWNLOAD_CONNECTIONS="$(read_download_limit KANBAN_DOWNLOAD_CONNECTIONS_PER_IP 2 100)"
DOWNLOAD_CONNECTIONS_TOTAL="$(read_download_limit KANBAN_DOWNLOAD_CONNECTIONS_TOTAL 8 1000)"
AVAILABLE="/etc/nginx/sites-available/$DOMAIN"
ENABLED="/etc/nginx/sites-enabled/$DOMAIN"
for target in "$AVAILABLE" "$ENABLED"; do
  if [[ -e "$target" && ! -L "$target" ]]; then
    echo "同名站点已存在，需先核对：$target" >&2; exit 1
  fi
done
[[ ! -L "$AVAILABLE" || "$(readlink "$AVAILABLE")" == "$ROOT/.deploy/nginx.conf" ]]
[[ ! -L "$ENABLED" || "$(readlink "$ENABLED")" == "$AVAILABLE" ]]
install -d -m 0755 .deploy .deploy/acme .deploy/downloads

activate() {
  sed -e "s|@DOMAIN@|$DOMAIN|g" \
    -e "s|@DOWNLOAD_RATE@|$DOWNLOAD_RATE|g" \
    -e "s|@DOWNLOAD_REQUESTS@|$DOWNLOAD_REQUESTS|g" \
    -e "s|@DOWNLOAD_REQUESTS_TOTAL@|$DOWNLOAD_REQUESTS_TOTAL|g" \
    -e "s|@DOWNLOAD_CONNECTIONS@|$DOWNLOAD_CONNECTIONS|g" \
    -e "s|@DOWNLOAD_CONNECTIONS_TOTAL@|$DOWNLOAD_CONNECTIONS_TOTAL|g" \
    "$1" > .deploy/nginx.conf.next
  chmod 0644 .deploy/nginx.conf.next
  if [[ -f .deploy/nginx.conf ]]; then cp .deploy/nginx.conf .deploy/nginx.conf.previous; fi
  mv -f .deploy/nginx.conf.next .deploy/nginx.conf
  ln -sfn "$ROOT/.deploy/nginx.conf" "$AVAILABLE"
  ln -sfn "$AVAILABLE" "$ENABLED"
  if ! nginx -t; then
    # 校验不通过时还原本项目配置，不让后续其他站点的 reload 被坏配置阻塞。
    if [[ -f .deploy/nginx.conf.previous ]]; then
      mv -f .deploy/nginx.conf.previous .deploy/nginx.conf
    else
      rm -f -- "$ENABLED" "$AVAILABLE"
    fi
    return 1
  fi
  systemctl reload nginx
}

if [[ ! -f "/etc/letsencrypt/live/$DOMAIN/fullchain.pem" ]]; then
  activate deploy/nginx.bootstrap.conf
  # 使用服务器已有 Certbot 注册账户，只签发本项目证书。
  certbot certonly --non-interactive --webroot -w "$ROOT/.deploy/acme" \
    --cert-name "$DOMAIN" -d "$DOMAIN" --keep-until-expiring \
    --deploy-hook 'nginx -t && systemctl reload nginx'
fi
activate deploy/nginx.conf
