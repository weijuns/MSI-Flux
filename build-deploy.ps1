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

# ── 4b. 产物验证（部署前必查，防止部署坏构建）──────────────────────
#
# 2026-10-19 事故：曾把 publish_single 里一个 FileVersion=0.0.0.0、
# 缺失 SDDL 的坏构建部署到桌面。后果是 IPC 管道 ACL 不对，GUI 连不上
# 服务、性能调节全部失效，但服务本身能跑，很难一眼看出问题。
#
# 因此部署前强制校验三项硬指标：
#   1. FileVersion 必须等于 csproj 的 <Version>
#   2. 二进制内必须含管道 ACL 的 SDDL 字符串
#   3. 体积必须在合理区间
# 任一不过直接中止，不进入部署。
Write-Step '产物验证 (部署前必查)'

$artifactItem = Get-Item $artifact
$fileVer = $artifactItem.VersionInfo.FileVersion

# 1. FileVersion
if ($fileVer -ne $version) {
    throw "产物版本不匹配: FileVersion='$fileVer'，期望 '$version'。可能是旧构建残留或 csproj 未更新。"
}
Write-Ok "FileVersion = $fileVer"

# 2. SDDL（IPC 管道 ACL）。缺失会导致 GUI 连不上服务。
#    字符串在程序集里以 UTF-16 存储，直接按 Unicode 解码搜索。
$bytes = [System.IO.File]::ReadAllBytes($artifact)
$text  = [System.Text.Encoding]::Unicode.GetString($bytes)
if (-not $text.Contains('O:BAG:SYD')) {
    throw "产物缺少 IPC 管道 ACL 的 SDDL 定义 (O:BAG:SYD)。这会导致 GUI 无法连接服务。"
}
if (-not $text.Contains('GRGW')) {
    throw "产物缺少管道 ACL 的 GRGW（读写）授权。普通用户将无法连接服务。"
}
Write-Ok '管道 ACL (SDDL) 完整'

# 3. 体积下限（防止空壳/截断产物）
if ($artifactItem.Length -lt 5000000) {
    throw "产物体积异常偏小: $($artifactItem.Length) 字节，疑似构建不完整。"
}
Write-Ok "体积 $($artifactItem.Length) 字节"

# 记录 MD5，方便和发布页/部署文件对照
$artifactMd5 = (Get-FileHash $artifact -Algorithm MD5).Hash
Write-Ok "MD5 $artifactMd5"

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
    Write-Warn "MSI Flux 正在运行 ($($running.Count) 个进程)，尝试自动停止以便替换文件"
    # 先停服务（服务进程也占着 exe），再结束 GUI 进程
    $svcRun = Get-Service -Name 'MSIFluxService' -ErrorAction SilentlyContinue
    if ($svcRun -and $svcRun.Status -ne 'Stopped') {
        try {
            Stop-Service -Name 'MSIFluxService' -Force -ErrorAction Stop
            Start-Sleep -Seconds 2
            Write-Ok '服务已停止'
        } catch {
            Write-Warn "停止服务失败（可能需要管理员）: $($_.Exception.Message)"
        }
    }
    Get-Process -Name 'MSI Flux' -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.Kill(); } catch { }
    }
    Start-Sleep -Seconds 2

    $still = Get-Process -Name 'MSI Flux' -ErrorAction SilentlyContinue
    if ($still) {
        throw "仍有 $($still.Count) 个 MSI Flux 进程无法结束，请手动从托盘退出后重试"
    }
    Write-Ok '所有 MSI Flux 进程已结束'
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

# ── 6b. 部署后验证：IPC 管道连通性 ────────────────────────────────
#
# 若后台服务正在运行，用普通用户身份试连一次主管道。
# 连得上说明管道 ACL 正确（GUI 能连服务）；连不上说明 ACL 有问题，
# 这正是 2026-10-19 那次坏构建的症状。
#
# 服务未运行时跳过（无管道可连，属正常情况）。
Write-Step '部署后验证'
$svc = Get-Service -Name 'MSIFluxService' -ErrorAction SilentlyContinue
if ($null -eq $svc) {
    Write-Warn 'MSIFluxService 未注册，跳过管道连通性检查'
}
elseif ($svc.Status -ne 'Running') {
    Write-Warn "服务状态为 $($svc.Status)，跳过管道连通性检查（需重启服务才生效）"
}
else {
    $pipeOk = $false
    $pipeErr = ''
    try {
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(
            '.', 'MSIFlux-Server', [System.IO.Pipes.PipeDirection]::InOut)
        try {
            $pipe.Connect(3000)
            $pipeOk = $true
        }
        finally { $pipe.Dispose() }
    }
    catch { $pipeErr = $_.Exception.Message }

    if (-not $pipeOk) {
        throw "IPC 管道连接失败（ACL 可能有误，GUI 将无法连上服务）: $pipeErr"
    }
    Write-Ok "IPC 管道连通 (MSIFlux-Server)"
}

# ── 7. 汇总 ───────────────────────────────────────────────────────
Write-Step '完成'
Write-Host "  版本   : v$version"
Write-Host "  体积   : $sizeMB MB"
Write-Host "  MD5    : $artifactMd5"
Write-Host "  位置   : $target"
Write-Host "  备份   : $BackupDir"
Write-Host ''
Write-Host '  提示: 若服务已注册，需重启服务才会加载新 exe：' -ForegroundColor DarkGray
Write-Host '        sc.exe stop MSIFluxService' -ForegroundColor DarkGray
Write-Host '        sc.exe start MSIFluxService' -ForegroundColor DarkGray
