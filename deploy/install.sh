#!/usr/bin/env bash
# Ubuntu / Debian 首次安装入口；也可再次运行以更新已有安装。
set -Eeuo pipefail

main() {
  if (( EUID != 0 )); then
    echo '请使用 sudo bash install.sh 执行。' >&2
    exit 1
  fi
  local gateway="${1:-standalone}"
  case "$gateway" in
    standalone|--existing-nginx) ;;
    *) echo '用法：sudo bash install.sh [--existing-nginx]' >&2; exit 1 ;;
  esac
  # 使用 Docker 官方软件源，不移除服务器上已有的容器或数据卷。
  source /etc/os-release
  case "$ID" in
    ubuntu|debian) ;;
    *) echo '自动安装支持 Ubuntu / Debian；其他系统请按部署文档安装 Docker。' >&2; exit 1 ;;
  esac
  if [[ "$gateway" == --existing-nginx ]]; then
    # 共享服务器复用现成环境，不安装软件包，也不重启已有 Docker 服务。
    for tool in docker nginx certbot curl git flock; do command -v "$tool" >/dev/null; done
    docker compose version >/dev/null
    systemctl is-active --quiet nginx
  else
    apt-get update
    apt-get install -y ca-certificates curl git util-linux
    if ! command -v docker >/dev/null || ! docker compose version >/dev/null 2>&1; then
      install -m 0755 -d /etc/apt/keyrings
      curl -fsSL "https://download.docker.com/linux/$ID/gpg" -o /etc/apt/keyrings/docker.asc
      chmod 0644 /etc/apt/keyrings/docker.asc
      cat > /etc/apt/sources.list.d/docker.sources <<EOF
Types: deb
URIs: https://download.docker.com/linux/$ID
Suites: ${UBUNTU_CODENAME:-$VERSION_CODENAME}
Components: stable
Architectures: $(dpkg --print-architecture)
Signed-By: /etc/apt/keyrings/docker.asc
EOF
      apt-get update
      if command -v docker >/dev/null; then
        apt-get install -y docker-buildx-plugin docker-compose-plugin
      else
        apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
      fi
    fi
    systemctl enable --now docker
  fi
  docker info >/dev/null

  # 公开仓库通过 HTTPS 只读拉取，服务器无需保存开发者的 GitHub 私钥。
  if [[ ! -d /opt/classglow/.git ]]; then
    git clone --branch main --single-branch https://github.com/2021Dt/ClassGlow.git /opt/classglow
  fi
  if [[ ! -f /opt/classglow/deploy/.env ]]; then
    install -m 0600 /opt/classglow/deploy/.env.example /opt/classglow/deploy/.env
  fi
  if [[ "$gateway" == --existing-nginx ]] && ! grep -qx 'KANBAN_GATEWAY=nginx' /opt/classglow/deploy/.env; then
    printf '\nKANBAN_GATEWAY=nginx\n' >> /opt/classglow/deploy/.env
  fi
  install -d /usr/local/bin
  cat > /usr/local/bin/classglow <<'EOF'
#!/usr/bin/env bash
exec bash /opt/classglow/deploy/manage.sh "$@"
EOF
  chmod 0755 /usr/local/bin/classglow
  classglow update
  echo '读取安装密钥：sudo classglow setup-key（请勿粘贴到公开日志）'
  echo '首次安装完成。打开 https://teacher.dgplus.vip 设置管理员。'
  echo '启用持续自动更新：sudo classglow auto-on；手动更新：sudo classglow update'
}

main "$@"
