param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\data\backups'))
$ErrorActionPreference = 'Stop'
# pg_dump 使用 PGPASSFILE 或既有连接配置，不把口令拼进参数和日志。
if (-not $env:PGDATABASE) { throw '请先通过 PGHOST、PGPORT、PGDATABASE、PGUSER 和 PGPASSFILE 配置数据库连接。' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$backupFile = Join-Path $OutputDirectory ("kanban-{0}.dump" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
& pg_dump --format=custom --file=$backupFile
if ($LASTEXITCODE -ne 0) { throw '数据库备份失败，不清理历史备份。' }
# 只清理本脚本命名且超过 14 天的普通文件，绝不递归删除目录。
Get-ChildItem -LiteralPath $OutputDirectory -File -Filter 'kanban-*.dump' | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-14) } | Remove-Item
Write-Output "备份完成：$backupFile"
