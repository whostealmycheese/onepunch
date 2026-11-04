# ==============================================================================
# 교육장 PC 원클릭 환경 구축 스크립트 (setup.ps1) - 멱등성 & 예외처리 강화 버전
# 대상: WSL (Ubuntu 24.04), Docker Desktop, Python, VS Code (한국어팩)
# ==============================================================================

#Requires -RunAsAdministrator

[CmdletBinding()]
param()

$ErrorActionPreference = "Continue"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12 -bor [System.Net.SecurityProtocolType]::Tls13

# 1. 경로 설정 및 로깅 초기화
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$InstallersDir = Join-Path $ScriptDir "installers"
if (-not (Test-Path $InstallersDir)) {
    New-Item -ItemType Directory -Path $InstallersDir -Force | Out-Null
}

$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$LogFile = Join-Path $ScriptDir "setup_$Timestamp.log"

function Log-Message {
    param([string]$Message, [string]$Level = "INFO")
    $timeStr = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $formatted = "[$timeStr] [$Level] $Message"
    Add-Content -Path $LogFile -Value $formatted -Encoding UTF8 -ErrorAction SilentlyContinue
    
    switch ($Level) {
        "SUCCESS" { Write-Host $Message -ForegroundColor Green }
        "WARN"    { Write-Host $Message -ForegroundColor Yellow }
        "ERROR"   { Write-Host $Message -ForegroundColor Red }
        "STEP"    { Write-Host "`n>>> $Message" -ForegroundColor Cyan }
        "SKIP"    { Write-Host "[-] $Message" -ForegroundColor DarkGray }
        default   { Write-Host $Message -ForegroundColor White }
    }
}

# 다운로드 헬퍼 (재시도 및 캐시 지원)
function Download-FileWithRetry {
    param([string]$Url, [string]$Destination, [int]$MaxRetries = 3)
    if (Test-Path $Destination) {
        $size = (Get-Item $Destination).Length
        if ($size -gt 1048576) { # 1MB 초과 시 기존 캐시 파일 사용
            Log-Message "기존 설치 파일 발견 ($([math]::Round($size/1MB, 2)) MB). 재다운로드를 건너뜁니다: $(Split-Path -Leaf $Destination)" "SUCCESS"
            return $true
        }
    }
    
    $attempt = 0
    while ($attempt -lt $MaxRetries) {
        $attempt++
        try {
            Log-Message "다운로드 시도 ($attempt/$MaxRetries): $Url" "INFO"
            $webClient = New-Object System.Net.WebClient
            $webClient.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)")
            $webClient.DownloadFile($Url, $Destination)
            Log-Message "다운로드 완료: $(Split-Path -Leaf $Destination)" "SUCCESS"
            return $true
        } catch {
            Log-Message "다운로드 $attempt차 시도 실패: $($_.Exception.Message)" "WARN"
            Start-Sleep -Seconds 2
        }
    }
    Log-Message "최종 다운로드 실패 ($Url). 네트워크 연결 또는 방화벽을 확인하세요." "ERROR"
    return $false
}

# 전역 환경 변수 브로드캐스트
function Broadcast-EnvChange {
    try {
        $signature = @'
[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
public static extern IntPtr SendMessageTimeout(
    IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam,
    uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);
'@
        $user32 = Add-Type -MemberDefinition $signature -Name "Win32SendMessageTimeout" -Namespace "Win32" -PassThru -ErrorAction SilentlyContinue
        if ($user32) {
            $HWND_BROADCAST = [IntPtr]0xffff
            $WM_SETTINGCHANGE = 0x001A
            $SMTO_ABORTIFHUNG = 0x0002
            $result = [UIntPtr]::Zero
            [Win32.Win32SendMessageTimeout]::SendMessageTimeout($HWND_BROADCAST, $WM_SETTINGCHANGE, [UIntPtr]::Zero, "Environment", $SMTO_ABORTIFHUNG, 5000, [ref]$result) | Out-Null
        }
    } catch {}
}

# 시스템 전역 PATH 안전 등록 (중복 제거 및 최상단 등록)
function Add-DirectoryToSystemPath {
    param([string]$DirPath)
    if (-not (Test-Path $DirPath)) { return }
    $cleanDir = (Resolve-Path $DirPath).Path.TrimEnd('\')
    
    # 1. 현재 프로세스 PATH 업데이트
    $procItems = $env:PATH -split ';' | Where-Object { $_ -ne "" } | ForEach-Object { $_.TrimEnd('\') }
    if ($procItems -notcontains $cleanDir) {
        $env:PATH = "$cleanDir;$env:PATH"
    }

    # 2. 시스템(Machine) 전역 PATH 레지스트리 업데이트
    try {
        $regPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment"
        $machinePath = (Get-ItemProperty -Path $regPath -Name "Path" -ErrorAction SilentlyContinue).Path
        if ($machinePath) {
            $rawItems = $machinePath -split ';' | Where-Object { $_ -ne "" }
            $cleanedList = New-Object System.Collections.Generic.List[string]
            
            # 최우선 순위로 추가
            $cleanedList.Add($cleanDir)
            foreach ($item in $rawItems) {
                $trimmed = $item.Trim().TrimEnd('\')
                if ($trimmed -and ($trimmed -ne $cleanDir) -and (-not $cleanedList.Contains($trimmed))) {
                    $cleanedList.Add($trimmed)
                }
            }
            $newMachinePath = ($cleanedList -join ';')
            Set-ItemProperty -Path $regPath -Name "Path" -Value $newMachinePath
            Log-Message "전역 PATH 최우선 등록 완료: $cleanDir" "SUCCESS"
        }
    } catch {
        Log-Message "전역 PATH 등록 중 예외: $($_.Exception.Message)" "WARN"
    }
}

# 바탕화면 바로가기 안전 생성 (공용 및 사용자 바탕화면)
function Create-DesktopShortcut {
    param(
        [string]$ShortcutName,
        [string]$TargetPath,
        [string]$Arguments = "",
        [string]$IconLocation = "",
        [string]$Description = ""
    )
    
    $desktopDirs = @()
    $commonDesktop = [Environment]::GetFolderPath('CommonDesktopDirectory')
    if (-not [string]::IsNullOrEmpty($commonDesktop) -and (Test-Path $commonDesktop)) {
        $desktopDirs += $commonDesktop
    } elseif (Test-Path "C:\Users\Public\Desktop") {
        $desktopDirs += "C:\Users\Public\Desktop"
    }
    
    $userDesktop = [Environment]::GetFolderPath('Desktop')
    if (-not [string]::IsNullOrEmpty($userDesktop) -and (Test-Path $userDesktop)) {
        $desktopDirs += $userDesktop
    } elseif (Test-Path "$env:USERPROFILE\Desktop") {
        $desktopDirs += "$env:USERPROFILE\Desktop"
    }
    
    $wscript = New-Object -ComObject WScript.Shell
    foreach ($dir in ($desktopDirs | Select-Object -Unique)) {
        try {
            $linkPath = Join-Path $dir "$ShortcutName.lnk"
            $shortcut = $wscript.CreateShortcut($linkPath)
            $shortcut.TargetPath = $TargetPath
            if ($Arguments) { $shortcut.Arguments = $Arguments }
            if ($IconLocation) { $shortcut.IconLocation = $IconLocation }
            if ($Description) { $shortcut.Description = $Description }
            $shortcut.Save()
            Log-Message "바탕화면 아이콘 등록됨: $ShortcutName ($dir)" "SUCCESS"
        } catch {
            Log-Message "바탕화면 아이콘 생성 예외 ($dir): $($_.Exception.Message)" "WARN"
        }
    }
}

# ==============================================================================
# 설치 감지 함수 모음 (Idempotency)
# ==============================================================================

function Detect-PythonInstallation {
    $info = @{
        Installed = $false
        Version = ""
        Path = ""
        Executable = ""
        ScriptsPath = ""
    }
    
    $searchPaths = @(
        "C:\Program Files\Python312", "C:\Program Files\Python311", "C:\Program Files\Python310", "C:\Program Files\Python313",
        "$env:LOCALAPPDATA\Programs\Python\Python312", "$env:LOCALAPPDATA\Programs\Python\Python311", "$env:LOCALAPPDATA\Programs\Python\Python310",
        "C:\Python312", "C:\Python311"
    )
    
    # 1. Registry 탐색
    $regRoots = @("HKLM:\SOFTWARE\Python\PythonCore", "HKCU:\SOFTWARE\Python\PythonCore")
    foreach ($root in $regRoots) {
        if (Test-Path $root) {
            Get-ChildItem -Path $root -ErrorAction SilentlyContinue | ForEach-Object {
                $installKey = Join-Path $_.PSPath "InstallPath"
                if (Test-Path $installKey) {
                    $p = (Get-ItemProperty -Path $installKey -Name "(default)" -ErrorAction SilentlyContinue)."(default)"
                    if (-not $p) {
                        $p = (Get-ItemProperty -Path $installKey -Name "ExecutablePath" -ErrorAction SilentlyContinue).ExecutablePath
                        if ($p) { $p = Split-Path -Parent $p }
                    }
                    if ($p -and (Test-Path $p)) { $searchPaths += $p }
                }
            }
        }
    }
    
    # 2. PATH 검색 (WindowsApps Stub 제외)
    try {
        $cmdPy = (Get-Command "python.exe" -ErrorAction SilentlyContinue).Source
        if ($cmdPy -and ($cmdPy -notmatch "WindowsApps")) {
            $searchPaths = @(Split-Path -Parent $cmdPy) + $searchPaths
        }
    } catch {}

    foreach ($dir in ($searchPaths | Select-Object -Unique)) {
        if (-not $dir) { continue }
        $exe = Join-Path $dir "python.exe"
        if (Test-Path $exe) {
            try {
                $v = $(& $exe -c "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}')" 2>&1 | Out-String).Trim()
                if ($v -match "^\d+\.\d+\.\d+") {
                    $info.Installed = $true
                    $info.Version = $v
                    $info.Path = $dir
                    $info.Executable = $exe
                    $s = Join-Path $dir "Scripts"
                    if (Test-Path $s) { $info.ScriptsPath = $s }
                    return $info
                }
            } catch {}
        }
    }
    return $info
}

function Detect-VSCodeInstallation {
    $info = @{
        Installed = $false
        Path = ""
        Executable = ""
        CodeCmd = ""
        KoreanExt = $false
    }
    
    $searchPaths = @(
        "C:\Program Files\Microsoft VS Code",
        "$env:LOCALAPPDATA\Programs\Microsoft VS Code"
    )
    
    foreach ($dir in ($searchPaths | Select-Object -Unique)) {
        $exe = Join-Path $dir "Code.exe"
        if (Test-Path $exe) {
            $info.Installed = $true
            $info.Path = $dir
            $info.Executable = $exe
            $cmd = Join-Path $dir "bin\code.cmd"
            if (Test-Path $cmd) { $info.CodeCmd = $cmd }
            break
        }
    }
    
    # 한국어 확장 설치 여부 확인
    if ($info.CodeCmd) {
        try {
            $extList = $(& $info.CodeCmd --list-extensions 2>&1 | Out-String)
            if ($extList -match "ms-ceintl\.vscode-language-pack-ko") {
                $info.KoreanExt = $true
            }
        } catch {}
    }
    
    return $info
}

function Detect-DockerInstallation {
    $info = @{
        Installed = $false
        Path = ""
        Executable = ""
        BinPath = ""
    }
    
    $localApp = $env:LOCALAPPDATA
    $candidateDirs = @(
        (Join-Path $localApp "Programs\DockerDesktop"),
        "C:\Program Files\Docker\Docker",
        "C:\Program Files\Docker\Docker Desktop",
        "C:\Program Files (x86)\Docker\Docker"
    )

    # where.exe docker 검색
    try {
        $whereDocker = $(where.exe docker 2>$null)
        if ($whereDocker) {
            foreach ($line in ($whereDocker -split "`r?`n")) {
                if (Test-Path $line) {
                    $b = Split-Path -Parent $line
                    $c = Split-Path -Parent $b
                    $parent = Split-Path -Parent $c
                    if ($parent -and (Test-Path $parent)) { $candidateDirs = @($parent) + $candidateDirs }
                }
            }
        }
    } catch {}

    foreach ($dir in ($candidateDirs | Select-Object -Unique)) {
        if (-not $dir) { continue }
        $exe = Join-Path $dir "Docker Desktop.exe"
        if (Test-Path $exe) {
            $info.Installed = $true
            $info.Path = $dir
            $info.Executable = $exe
            $b1 = Join-Path $dir "resources\bin"
            $b2 = Join-Path $dir "bin"
            if (Test-Path $b1) { $info.BinPath = $b1 }
            elseif (Test-Path $b2) { $info.BinPath = $b2 }
            else { $info.BinPath = $dir }
            return $info
        }
    }

    # CLI 명령어로도 감지
    try {
        $ver = $(docker.exe --version 2>&1 | Out-String)
        if ($ver -match "Docker version") {
            $info.Installed = $true
            $info.Path = "Docker in PATH"
            $info.Executable = "docker.exe"
        }
    } catch {}

    return $info
}

function Detect-WSLInstallation {
    $info = @{
        WSLAvailable = $false
        Ubuntu24Installed = $false
        DistroName = ""
        ExistingDistros = @()
    }
    
    $wslCandidates = @("C:\Windows\System32\wsl.exe", "C:\Program Files\WSL\wsl.exe")
    $wslExe = ""
    foreach ($cand in $wslCandidates) {
        if (Test-Path $cand) { $wslExe = $cand; break }
    }
    if (-not $wslExe) {
        $whereWsl = $(where.exe wsl 2>$null)
        if ($whereWsl) { $wslExe = ($whereWsl -split "`r?`n")[0] }
    }
    
    if ($wslExe -and (Test-Path $wslExe)) {
        $info.WSLAvailable = $true
        try {
            $rawOutput = $(& $wslExe -l -q 2>&1 | Out-String)
            $cleanOutput = $rawOutput.Replace("`0", "").Trim()
            $lines = $cleanOutput -split "`r?`n" | Where-Object { $_ -match "\S" }
            $info.ExistingDistros = $lines
            foreach ($line in $lines) {
                if ($line -match "Ubuntu") {
                    $info.Ubuntu24Installed = $true
                    $info.DistroName = $line.Trim()
                    break
                }
            }
        } catch {}
    }
    return $info
}

# ==============================================================================
# 시스템 사전 점검 (Pre-Flight Checks)
# ==============================================================================

function Perform-PreFlightChecks {
    Log-Message "[사전 점검] 시스템 및 하드웨어 환경 진단 중..." "STEP"
    
    # 1. OS 버전 및 빌드 점검
    $os = [System.Environment]::OSVersion
    Log-Message "운영체제: Windows $($os.Version.Major) (빌드: $($os.Version.Build))" "INFO"
    if ($os.Version.Build -lt 19041) {
        Log-Message "경고: WSL2를 원활하게 구동하려면 Windows 10 버전 2004(빌드 19041) 이상이 필요합니다." "WARN"
    }

    # 2. C: 드라이브 여유 디스크 공간 점검
    try {
        $cDrive = Get-PSDrive C -ErrorAction SilentlyContinue
        if ($cDrive) {
            $freeGB = [math]::Round($cDrive.Free / 1GB, 1)
            Log-Message "C: 드라이브 여유 공간: $freeGB GB" "INFO"
            if ($freeGB -lt 15) {
                Log-Message "경고: C: 드라이브 여유 공간이 15GB 미만입니다 ($freeGB GB). 도커 및 리눅스 이미지 설치 시 용량이 부족할 수 있습니다." "WARN"
            }
        }
    } catch {}

    # 3. CPU 하드웨어 가상화 (VT-x / AMD-V) 점검
    try {
        $virtEnabled = $null
        $proc = Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($proc -and ($proc.PSObject.Properties['VirtualizationFirmwareEnabled'])) {
            $virtEnabled = $proc.VirtualizationFirmwareEnabled
        }
        if ($virtEnabled -eq $false) {
            Log-Message "======================================================================" "WARN"
            Log-Message "주의: 메인보드 BIOS/UEFI에서 CPU 가상화(VT-x / SVM)가 비활성화되어 있습니다!" "WARN"
            Log-Message "WSL2 및 Docker Desktop 정상 작동을 위해 BIOS에서 가상화 기능을 [Enabled]로 변경해야 합니다." "WARN"
            Log-Message "======================================================================" "WARN"
        } elseif ($virtEnabled -eq $true) {
            Log-Message "하드웨어 가상화(VT-x / SVM): 활성화 확인됨" "SUCCESS"
        }
    } catch {}

    # 4. 실행 중인 충돌 프로세스 점검
    $conflictProcs = @("Code", "Docker Desktop", "python")
    foreach ($p in $conflictProcs) {
        $running = Get-Process -Name $p -ErrorAction SilentlyContinue
        if ($running) {
            Log-Message "알림: '$p' 프로세스가 현재 실행 중입니다. 파일 잠금 방지를 위해 작업 종료를 권장합니다." "WARN"
        }
    }
}

# ==============================================================================
# 메인 실행 흐름
# ==============================================================================

Clear-Host
Log-Message "======================================================================" "INFO"
Log-Message "     교육장 PC 원클릭 개발 환경 구축 솔루션 (멱등성 & 예외처리 강화)   " "INFO"
Log-Message "======================================================================" "INFO"
Log-Message "로그 파일 위치: $LogFile" "INFO"

Perform-PreFlightChecks

$finalSummary = @()

# ------------------------------------------------------------------------------
# STEP 1: Python 점검 및 설치 / 환경 복구
# ------------------------------------------------------------------------------
Log-Message "[1/4] Python 점검 및 환경 구성..." "STEP"
$pyInfo = Detect-PythonInstallation

if ($pyInfo.Installed) {
    Log-Message "이미 설치된 Python 감지: 버전 $($pyInfo.Version) (경로: $($pyInfo.Path))" "SKIP"
    Log-Message "신규 다운로드 및 설치를 건너뛰고 환경변수 및 바로가기 점검을 수행합니다." "INFO"
    $pyTargetDir = $pyInfo.Path
    $pyStatus = "기존 설치 유지 ($($pyInfo.Version))"
} else {
    Log-Message "Python이 설치되어 있지 않습니다. 신규 무인 설치를 진행합니다." "INFO"
    $pythonVersion = "3.12.8"
    $pythonUrl = "https://www.python.org/ftp/python/$pythonVersion/python-$pythonVersion-amd64.exe"
    $pythonInstaller = Join-Path $InstallersDir "python-$pythonVersion-amd64.exe"
    $pyTargetDir = "C:\Program Files\Python312"

    $downloadOk = Download-FileWithRetry -Url $pythonUrl -Destination $pythonInstaller
    if ($downloadOk -and (Test-Path $pythonInstaller)) {
        Log-Message "Python 3.12 무인 설치 실행 중..." "INFO"
        $pyArgs = "/quiet InstallAllUsers=1 PrependPath=1 Include_test=0 Include_pip=1 TargetDir=`"$pyTargetDir`""
        $proc = Start-Process -FilePath $pythonInstaller -ArgumentList $pyArgs -Wait -PassThru
        if ($proc.ExitCode -eq 0) {
            Log-Message "Python 3.12 설치 성공" "SUCCESS"
            $pyStatus = "신규 설치 완료 (3.12.8)"
        } else {
            Log-Message "Python 설치 종료 코드: $($proc.ExitCode)" "WARN"
            $pyStatus = "설치 경고 (코드: $($proc.ExitCode))"
        }
    } else {
        Log-Message "Python 다운로드 실패. 네트워크를 확인하세요." "ERROR"
        $pyStatus = "설치 실패 (다운로드 오류)"
    }
}

# Python 환경변수 및 App Paths 보완 (설치 여부와 무관하게 무조건 보장)
if (Test-Path $pyTargetDir) {
    Add-DirectoryToSystemPath -DirPath $pyTargetDir
    $scriptsDir = Join-Path $pyTargetDir "Scripts"
    if (Test-Path $scriptsDir) { Add-DirectoryToSystemPath -DirPath $scriptsDir }
    
    # Windows Store Alias 충돌 방지 레지스트리
    try {
        $appPathsKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"
        $pyExe = Join-Path $pyTargetDir "python.exe"
        if (Test-Path $pyExe) {
            New-Item -Path "$appPathsKey\python.exe" -Value $pyExe -Force -ErrorAction SilentlyContinue | Out-Null
            Set-ItemProperty -Path "$appPathsKey\python.exe" -Name "Path" -Value $pyTargetDir -Force -ErrorAction SilentlyContinue
            New-Item -Path "$appPathsKey\python3.exe" -Value $pyExe -Force -ErrorAction SilentlyContinue | Out-Null
            Set-ItemProperty -Path "$appPathsKey\python3.exe" -Name "Path" -Value $pyTargetDir -Force -ErrorAction SilentlyContinue
        }
    } catch {}

    # 바로가기 생성
    Create-DesktopShortcut -ShortcutName "Python (명령줄)" -TargetPath (Join-Path $pyTargetDir "python.exe") -IconLocation (Join-Path $pyTargetDir "python.exe,0") -Description "Python REPL"
    $idleScript = Join-Path $pyTargetDir "Lib\idlelib\idle.pyw"
    $pywExe = Join-Path $pyTargetDir "pythonw.exe"
    if ((Test-Path $pywExe) -and (Test-Path $idleScript)) {
        Create-DesktopShortcut -ShortcutName "IDLE (Python GUI)" -TargetPath $pywExe -Arguments "`"$idleScript`"" -IconLocation (Join-Path $pyTargetDir "python.exe,0") -Description "Python IDLE"
    }
}
$finalSummary += [PSCustomObject]@{'Item' = "Python (CMD: python)"; 'Status' = $pyStatus}

# ------------------------------------------------------------------------------
# STEP 2: Visual Studio Code 및 한국어 언어팩 점검 및 설치
# ------------------------------------------------------------------------------
Log-Message "[2/4] Visual Studio Code 및 한국어 언어팩 점검..." "STEP"
$codeInfo = Detect-VSCodeInstallation
$vscodeTargetDir = "C:\Program Files\Microsoft VS Code"

if ($codeInfo.Installed) {
    Log-Message "이미 설치된 VS Code 감지 (경로: $($codeInfo.Path))" "SKIP"
    $vscodeTargetDir = $codeInfo.Path
    $codeCmd = $codeInfo.CodeCmd
    $codeStatus = "기존 설치 유지"
} else {
    Log-Message "VS Code가 설치되어 있지 않습니다. 신규 시스템 무인 설치를 진행합니다." "INFO"
    $vscodeUrl = "https://update.code.visualstudio.com/latest/win32-x64/stable"
    $vscodeInstaller = Join-Path $InstallersDir "VSCodeSetup-x64.exe"
    
    $vscodeDlOk = Download-FileWithRetry -Url $vscodeUrl -Destination $vscodeInstaller
    if ($vscodeDlOk -and (Test-Path $vscodeInstaller)) {
        $codeArgs = "/VERYSILENT /NORESTART /MERGETASKS=!runcode,addtopath,desktopicon,quicklaunchicon"
        $proc = Start-Process -FilePath $vscodeInstaller -ArgumentList $codeArgs -Wait -PassThru
        Log-Message "VS Code 설치 완료 (종료 코드: $($proc.ExitCode))" "SUCCESS"
        $codeCmd = Join-Path $vscodeTargetDir "bin\code.cmd"
        $codeStatus = "신규 설치 완료"
    } else {
        $codeStatus = "설치 실패 (다운로드 오류)"
    }
}

# 한국어 언어팩 점검 및 설치
if ($codeCmd -and (Test-Path $codeCmd)) {
    # PATH 등록
    Add-DirectoryToSystemPath -DirPath (Split-Path -Parent $codeCmd)
    
    if ($codeInfo.KoreanExt) {
        Log-Message "한국어 언어팩(MS-CEINTL.vscode-language-pack-ko)이 이미 설치되어 있습니다." "SKIP"
    } else {
        Log-Message "한국어 언어팩 설치 진행 중..." "INFO"
        Start-Process -FilePath $codeCmd -ArgumentList "--install-extension MS-CEINTL.vscode-language-pack-ko --force" -Wait -NoNewWindow
        Log-Message "한국어 언어팩 설치 완료" "SUCCESS"
    }
    
    # argv.json 언어 세팅
    try {
        $appDataCode = Join-Path $env:APPDATA "Code\User"
        if (-not (Test-Path $appDataCode)) { New-Item -ItemType Directory -Path $appDataCode -Force | Out-Null }
        $argvJson = Join-Path $appDataCode "argv.json"
        $cfg = @{ "locale" = "ko" }
        if (Test-Path $argvJson) {
            try {
                $existing = Get-Content $argvJson -Raw | ConvertFrom-Json
                $existing.locale = "ko"
                $existing | ConvertTo-Json | Set-Content -Path $argvJson -Encoding UTF8
            } catch { $cfg | ConvertTo-Json | Set-Content -Path $argvJson -Encoding UTF8 }
        } else {
            $cfg | ConvertTo-Json | Set-Content -Path $argvJson -Encoding UTF8
        }
    } catch {}
    
    # 바탕화면 바로가기 생성
    $codeExe = Join-Path $vscodeTargetDir "Code.exe"
    if (Test-Path $codeExe) {
        Create-DesktopShortcut -ShortcutName "Visual Studio Code" -TargetPath $codeExe -IconLocation "$codeExe,0" -Description "VS Code"
    }
}
$finalSummary += [PSCustomObject]@{'Item' = "VS Code (한국어팩 포함)"; 'Status' = $codeStatus}

# ------------------------------------------------------------------------------
# STEP 3: Docker Desktop 점검 및 설치
# ------------------------------------------------------------------------------
Log-Message "[3/4] Docker Desktop 점검 및 설치..." "STEP"
$dockerInfo = Detect-DockerInstallation
$dockerTargetDir = "C:\Program Files\Docker\Docker"

if ($dockerInfo.Installed) {
    Log-Message "이미 설치된 Docker Desktop 감지 (경로: $($dockerInfo.Path))" "SKIP"
    $dockerTargetDir = $dockerInfo.Path
    $dockerStatus = "기존 설치 유지"
} else {
    Log-Message "Docker Desktop이 설치되어 있지 않습니다. 신규 무인 설치를 진행합니다." "INFO"
    $dockerUrl = "https://desktop.docker.com/win/main/amd64/Docker%20Desktop%20Installer.exe"
    $dockerInstaller = Join-Path $InstallersDir "DockerDesktopInstaller.exe"
    
    $dockerDlOk = Download-FileWithRetry -Url $dockerUrl -Destination $dockerInstaller
    if ($dockerDlOk -and (Test-Path $dockerInstaller)) {
        Log-Message "Docker Desktop 설치 실행 중..." "INFO"
        $dockerArgs = "install --quiet --accept-license --backend=wsl-2"
        $proc = Start-Process -FilePath $dockerInstaller -ArgumentList $dockerArgs -Wait -PassThru
        Log-Message "Docker Desktop 설치 완료 (종료 코드: $($proc.ExitCode))" "SUCCESS"
        $dockerStatus = "신규 설치 완료"
    } else {
        $dockerStatus = "설치 실패 (다운로드 오류)"
    }
}

# Docker 환경변수 및 바로가기 보완
$dockerBinDir = Join-Path $dockerTargetDir "resources\bin"
if (Test-Path $dockerBinDir) { Add-DirectoryToSystemPath -DirPath $dockerBinDir }
$dockerDesktopExe = Join-Path $dockerTargetDir "Docker Desktop.exe"
if (Test-Path $dockerDesktopExe) {
    Create-DesktopShortcut -ShortcutName "Docker Desktop" -TargetPath $dockerDesktopExe -IconLocation "$dockerDesktopExe,0" -Description "Docker Desktop"
}
$finalSummary += [PSCustomObject]@{'Item' = "Docker Desktop (WSL2)"; 'Status' = $dockerStatus}

# ------------------------------------------------------------------------------
# STEP 4: WSL2 및 Ubuntu 24.04 점검 및 설치
# ------------------------------------------------------------------------------
Log-Message "[4/4] WSL2 및 Ubuntu 24.04 점검 및 설정..." "STEP"
$wslInfo = Detect-WSLInstallation

# 가상화 기능 활성화 보장
try {
    dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart | Out-Null
    dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart | Out-Null
    wsl.exe --set-default-version 2 2>&1 | Out-Null
} catch {}

if ($wslInfo.Ubuntu24Installed) {
    Log-Message "Ubuntu 24.04 LTS 가 이미 등록되어 있습니다." "SKIP"
    $wslStatus = "기존 배포판 유지 (Ubuntu-24.04)"
} else {
    Log-Message "Ubuntu 24.04 배포판 등록 진행 중 (wsl --install -d Ubuntu-24.04 --no-launch)..." "INFO"
    try {
        $output = $(wsl.exe --install -d Ubuntu-24.04 --no-launch 2>&1 | Out-String)
        Log-Message "WSL 명령 결과: $output" "INFO"
        $wslStatus = "신규 배포판 등록 완료"
    } catch {
        Log-Message "WSL 배포판 설치 예외: $($_.Exception.Message)" "WARN"
        $wslStatus = "설치 중 알림 (재부팅 필요할 수 있음)"
    }
}

# WSL 바로가기 보완 (꺼짐 방지 대화형 cmd /k 래퍼)
$distroName = if ($wslInfo.DistroName) { $wslInfo.DistroName } else { "Ubuntu-24.04" }
$wslTarget = "C:\Windows\System32\cmd.exe"
$wslArgs = "/k `"wsl.exe ~ -d $distroName 2>nul || wsl.exe ~ -d Ubuntu 2>nul || wsl.exe ~ 2>nul || (echo. && echo [WSL 실행 안내] WSL 배포판을 시작할 수 없습니다. && echo 'wsl -l -v' 로 등록 상태를 확인하세요. && pause)`""
Create-DesktopShortcut -ShortcutName "Ubuntu 24.04 LTS" -TargetPath $wslTarget -Arguments $wslArgs -IconLocation "C:\Windows\System32\wsl.exe,0" -Description "Ubuntu Linux Terminal"

# Windows Terminal 발견 시 터미널 바로가기 추가 생성
$whereWt = $(where.exe wt 2>$null)
if ($whereWt) {
    Create-DesktopShortcut -ShortcutName "Ubuntu (Windows Terminal)" -TargetPath "C:\Windows\System32\cmd.exe" -Arguments "/c start wt.exe wsl.exe ~ -d $distroName" -IconLocation "C:\Windows\System32\wsl.exe,0" -Description "Ubuntu in Windows Terminal"
}
$finalSummary += [PSCustomObject]@{'Item' = "WSL2 Ubuntu 24.04"; 'Status' = $wslStatus}

# 환경변수 브로드캐스트
Broadcast-EnvChange

# ------------------------------------------------------------------------------
# 최종 결과 보고서
# ------------------------------------------------------------------------------
Write-Host "`n======================================================================" -ForegroundColor Green
Write-Host "                    최종 환경 구축 및 점검 결과                      " -ForegroundColor Green
Write-Host "======================================================================" -ForegroundColor Green
$finalSummary | Format-Table -AutoSize | Out-String | Write-Host -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Green
Log-Message "모든 작업이 안전하게 완료되었습니다." "SUCCESS"
Log-Message "새 CMD 창을 열거나 'verify_env.bat'를 실행하여 명령어를 검증하세요." "INFO"
