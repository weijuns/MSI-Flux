# MSI Flux — 构建并部署到桌面（体积优先）
#
# 用法：
#   .\build-deploy.ps1              # 构建 + 部署
#   .\build-deploy.ps1 -SkipTests   # 跳过测试（不推荐）
#   .\build-deploy.ps1 -WhatIf      # 只看体积，不部署
#
# 约定（来自 HANDOFF.md）：
#   - 产物复制到 C:\Users\wjs\Desktop\MSI Flux\
#   - 框架依赖单文件（体积最优，约 9-10 MB），需要用户机安装 .NET 8 Desktop Runtime
#
# 为什么不用别的配置（体积对比，实测）：
#   publish-framework / publish-170   ≈  9.4 MB   ← 采用
#   publish-r2r (ReadyToRun)          ≈ 15.2 MB   +62%
#   publish-launcher (内嵌 exe)       ≈ 20.4 MB   +117%
#   publish_single (自包含)           ≈ 72.1 MB   +667%
#   publish_direct (非单文件)         ≈ 163 MB    ×17

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ── 路径 ──────────────────────────────────────────────────────────
$RepoRoot   = $PSScriptRoot
$ProjectDir = Join-Path $RepoRoot 'MSIFlux'
$Csproj     = Join-Path $ProjectDir 'MSIFlux.csproj'
$Sln        = Join-Path $RepoRoot 'MSIFlux.sln'
$TestProj   = Join-Path $RepoRoot 'MSIFlux.Tests\MSIFlux.Tests.csproj'
$PublishDir = Join-Path $RepoRoot 'publish-deploy'
$DeployDir  = 'C:\Users\wjs\Desktop\MSI Flux'
$BackupDir  = Join-Path $DeployDir '_backup'

function Write-Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  OK  $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "  !   $msg" -ForegroundColor Yellow }

# ── 1. 读版本号（单一来源: csproj 的 <Version>）────────────────────
Write-Step '读取版本号'
[xml]$proj = Get-Content $Csproj -Encoding UTF8
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } |
            Select-Object -First 1).Version
if (-not $version) { throw "csproj 里找不到 <Version>" }
Write-Ok "version = $version"

# ── 2. 构建 ───────────────────────────────────────────────────────
Write-Step '构建 (Release)'
$buildOut = dotnet build $Sln -c Release -v q --nologo 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    Write-Host $buildOut
    throw "构建失败"
}
# 只汇总统计，不打印几百行 NuGet 告警
$errCount  = ([regex]::Matches($buildOut, ': error ')).Count
$nugetWarn = ([regex]::Matches($buildOut, 'warning NU\d+')).Count
$codeWarn  = ([regex]::Matches($buildOut, 'warning (?!NU\d)\w+')).Count
Write-Ok "构建通过 (错误 $errCount / 代码告警 $codeWarn)"
if ($nugetWarn -gt 0) {
    Write-Warn "$nugetWarn 条 NuGet 安全告警 (MessagePack 3.1.4 有已知漏洞，建议另开任务升级)"
}

# ── 3. 测试 ───────────────────────────────────────────────────────
if (-not $SkipTests) {
    Write-Step '单元测试'
    $out = dotnet test $TestProj -c Release --nologo 2>&1 | Out-String
    $line = ($out -split "`n" | Where-Object { $_ -match '已通过|失败!|Passed!|Failed!' } |
             Select-Object -Last 1)
    if ($LASTEXITCODE -ne 0) {
        Write-Host $out
        throw "测试失败"
    }
    Write-Ok ($line.Trim())
} else {
    Write-Warn '已跳过测试'
}

# ── 4. 发布（框架依赖单文件，体积最优）────────────────────────────
Write-Step '发布 (framework-dependent single-file)'
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish $Csproj -c Release -o $PublishDir -r win-x64 `
    /p:PublishSingleFile=true `
    /p:SelfContained=false `
    /p:PublishReadyToRun=false `
    /p:DebugType=none `
    /p:NuGetAudit=false `
    --nologo -v q 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "发布失败" }

$artifact = Join-Path $PublishDir 'MSI Flux.exe'
if (-not (Test-Path $artifact)) { throw "产物不存在: $artifact" }

$sizeMB = [math]::Round((Get-Item $artifact).Length / 1MB, 2)
Write-Ok "产物 $sizeMB MB"

# 体积守门：框架依赖版不应超过 12 MB（超了说明误开了 R2R/自包含）
if ($sizeMB -gt 12) {
    Write-Warn "体积 $sizeMB MB 超出预期 (9-11 MB)，请检查是否误开了 PublishReadyToRun/SelfContained"
}

# ── 5. 冒烟测试（不触发 GUI / 不改服务状态）────────────────────────
Write-Step '冒烟测试'
$proc = Start-Process -FilePath $artifact -ArgumentList '--stop-service' `
        -PassThru -WindowStyle Hidden
if (-not $proc.WaitForExit(20000)) {
    $proc.Kill()
    throw "冒烟测试超时：进程 20s 未退出"
}
# --stop-service 在非管理员下返回 2（权限不足），这是预期值
if ($proc.ExitCode -notin @(0, 2, 3)) {
    throw "冒烟测试失败：退出码 $($proc.ExitCode)"
}
Write-Ok "进程可启动并正常退出 (ExitCode=$($proc.ExitCode))"

# ── 6. 部署 ───────────────────────────────────────────────────────
Write-Step "部署到 $DeployDir"
if (-not (Test-Path $DeployDir)) { New-Item -ItemType Directory -Path $DeployDir | Out-Null }

$running = Get-Process -Name 'MSI Flux' -ErrorAction SilentlyContinue
if ($running) {
    Write-Warn "MSI Flux 正在运行 ($($running.Count) 个进程)，文件可能被占用"
    Write-Warn '请先从托盘退出程序，或先执行: sc.exe stop MSIFluxService'
}

New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
$target = Join-Path $DeployDir 'MSI Flux.exe'

if (Test-Path $target) {
    $oldVer = (Get-Item $target).VersionInfo.FileVersion
    $stamp = Get-Date -Format 'yyyy-MM-dd_HHmmss'
    $bakName = "MSI Flux v$oldVer ($stamp).exe"
    Copy-Item $target (Join-Path $BackupDir $bakName) -Force
    Write-Ok "已备份旧版 → _backup\$bakName"
}

if ($PSCmdlet.ShouldProcess($target, "复制 v$version")) {
    Copy-Item $artifact $target -Force
    $h1 = (Get-FileHash $artifact -Algorithm SHA256).Hash
    $h2 = (Get-FileHash $target   -Algorithm SHA256).Hash
    if ($h1 -ne $h2) { throw '部署后哈希不一致' }
    Write-Ok "已部署 v$version ($sizeMB MB)，哈希校验通过"
}

# ── 7. 汇总 ───────────────────────────────────────────────────────
Write-Step '完成'
Write-Host "  版本   : v$version"
Write-Host "  体积   : $sizeMB MB"
Write-Host "  位置   : $target"
Write-Host "  备份   : $BackupDir"
Write-Host ''
Write-Host '  提示: 若服务已注册，需重启服务才会加载新 exe：' -ForegroundColor DarkGray
Write-Host '        sc.exe stop MSIFluxService' -ForegroundColor DarkGray
Write-Host '        sc.exe start MSIFluxService' -ForegroundColor DarkGray
