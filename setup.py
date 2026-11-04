# -*- coding: utf-8 -*-
"""
교육장 PC 원클릭 개발 환경 구축 자동화 스크립트 (Python 버전) - 멱등성 & 예외처리 강화
대상: WSL2 (Ubuntu 24.04), Docker Desktop, Python, VS Code (한국어 언어팩)
"""

import os
import sys
import ctypes
import subprocess
import urllib.request
import winreg
import json
import shutil
import time
from pathlib import Path

# 관리자 권한 확인 및 자동 승격 (UAC)
def is_admin():
    try:
        return ctypes.windll.shell32.IsUserAnAdmin()
    except Exception:
        return False

def elevate():
    if not is_admin():
        print("[안내] 관리자 권한이 필요합니다. 관리자 권한 승격(UAC)을 호출합니다...")
        script = os.path.abspath(sys.argv[0])
        params = " ".join([f'"{arg}"' for arg in sys.argv[1:]])
        ret = ctypes.windll.shell32.ShellExecuteW(
            None, "runas", sys.executable, f'"{script}" {params}', None, 1
        )
        sys.exit(0)

# 다운로드 헬퍼 (재시도 및 캐시 지원)
def download_file_with_retry(url, dest_path, max_retries=3):
    dest = Path(dest_path)
    if dest.exists() and dest.stat().st_size > 1048576:
        print(f"[*] 기존 설치 파일 캐시 발견 ({dest.stat().st_size / (1024*1024):.1f} MB): {dest.name}")
        return True
    
    for attempt in range(1, max_retries + 1):
        try:
            print(f"[*] 다운로드 시도 ({attempt}/{max_retries}): {url}")
            req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'})
            with urllib.request.urlopen(req, timeout=30) as response, open(dest_path, 'wb') as out_file:
                total_size = int(response.headers.get('Content-Length', 0))
                downloaded = 0
                block_size = 65536
                while True:
                    buffer = response.read(block_size)
                    if not buffer:
                        break
                    downloaded += len(buffer)
                    out_file.write(buffer)
                    if total_size > 0:
                        percent = downloaded * 100 / total_size
                        print(f"\r    진행률: {percent:.1f}% ({downloaded // (1024*1024)}MB / {total_size // (1024*1024)}MB)", end='')
            print("\n[+] 다운로드 완료!")
            return True
        except Exception as e:
            print(f"\n[-] {attempt}차 다운로드 실패: {e}")
            time.sleep(2)
            
    print(f"[-] 최종 다운로드 실패: {url}")
    return False

# 시스템 전역 PATH 추가 (중복 방지 및 최우선 순위 prepend)
def add_to_machine_path(directory):
    if not os.path.exists(directory):
        return
    clean_dir = os.path.abspath(directory).rstrip('\\')
    
    # 1. 프로세스 PATH 반영
    curr_paths = [p.rstrip('\\') for p in os.environ.get("PATH", "").split(os.pathsep) if p]
    if clean_dir not in curr_paths:
        os.environ["PATH"] = clean_dir + os.pathsep + os.environ["PATH"]

    # 2. 시스템 Machine PATH 레지스트리 갱신
    try:
        reg_key = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", 0, winreg.KEY_ALL_ACCESS)
        machine_path, _ = winreg.QueryValueEx(reg_key, "Path")
        items = [p.strip().rstrip('\\') for p in machine_path.split(";") if p.strip()]
        
        cleaned = [clean_dir] + [p for p in items if p != clean_dir]
        new_path = ";".join(cleaned)
        winreg.SetValueEx(reg_key, "Path", 0, winreg.REG_EXPAND_SZ, new_path)
        winreg.CloseKey(reg_key)
        print(f"[+] 시스템 전역 PATH 최우선 등록 완료: {clean_dir}")
    except Exception as e:
        print(f"[-] PATH 레지스트리 쓰기 예외 ({clean_dir}): {e}")

# 바탕화면 바로가기 생성
def create_shortcut(name, target, arguments="", icon_location="", description=""):
    ps_cmd = f"""
    $wscript = New-Object -ComObject WScript.Shell
    $desktops = @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('CommonDesktopDirectory'), "C:\\Users\\Public\\Desktop") | Where-Object {{ Test-Path $_ }} | Select-Object -Unique
    foreach ($d in $desktops) {{
        $link = Join-Path $d "{name}.lnk"
        $s = $wscript.CreateShortcut($link)
        $s.TargetPath = "{target}"
        if ("{arguments}") {{ $s.Arguments = "{arguments}" }}
        if ("{icon_location}") {{ $s.IconLocation = "{icon_location}" }}
        if ("{description}") {{ $s.Description = "{description}" }}
        $s.Save()
    }}
    """
    subprocess.run(["powershell", "-NoProfile", "-Command", ps_cmd], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

# 사전 진단 (Pre-Flight Checks)
def pre_flight_checks():
    print("\n[사전 점검] 시스템 및 하드웨어 환경 진단 중...")
    
    # 1. 디스크 공간 점검
    total, used, free = shutil.disk_usage("C:\\")
    free_gb = free // (2**30)
    print(f"  - C: 드라이브 여유 공간: {free_gb} GB")
    if free_gb < 15:
        print(f"  [경고] 디스크 여유 공간이 15GB 미만입니다. 설치 중 용량이 부족할 수 있습니다.")

    # 2. 실행 중인 프로세스 점검
    for proc in ["Code.exe", "Docker Desktop.exe", "python.exe"]:
        output = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {proc}"], capture_output=True, text=True).stdout
        if proc.lower() in output.lower():
            print(f"  [알림] '{proc}' 프로세스가 실행 중입니다. 안전한 설치를 위해 저장을 권장합니다.")

# 설치 감지 함수 모음
def detect_python():
    search_dirs = [
        r"C:\Program Files\Python312", r"C:\Program Files\Python311", r"C:\Program Files\Python310", r"C:\Program Files\Python313",
        rf"{os.environ.get('LOCALAPPDATA', '')}\Programs\Python\Python312",
        rf"{os.environ.get('LOCALAPPDATA', '')}\Programs\Python\Python311",
        r"C:\Python312", r"C:\Python311"
    ]
    for d in search_dirs:
        py_exe = os.path.join(d, "python.exe")
        if os.path.exists(py_exe):
            try:
                res = subprocess.run([py_exe, "-c", "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}')"], capture_output=True, text=True, timeout=5)
                ver = res.stdout.strip()
                if ver:
                    return {"installed": True, "path": d, "version": ver, "exe": py_exe}
            except Exception:
                pass
    return {"installed": False, "path": "", "version": "", "exe": ""}

def detect_vscode():
    search_dirs = [
        r"C:\Program Files\Microsoft VS Code",
        rf"{os.environ.get('LOCALAPPDATA', '')}\Programs\Microsoft VS Code"
    ]
    for d in search_dirs:
        code_exe = os.path.join(d, "Code.exe")
        if os.path.exists(code_exe):
            cmd = os.path.join(d, "bin", "code.cmd")
            korean_ext = False
            if os.path.exists(cmd):
                try:
                    exts = subprocess.run([cmd, "--list-extensions"], capture_output=True, text=True, timeout=10).stdout
                    if "ms-ceintl.vscode-language-pack-ko" in exts.lower():
                        korean_ext = True
                except Exception:
                    pass
            return {"installed": True, "path": d, "exe": code_exe, "cmd": cmd, "korean_ext": korean_ext}
    return {"installed": False, "path": "", "exe": "", "cmd": "", "korean_ext": False}

def detect_docker():
    path = r"C:\Program Files\Docker\Docker"
    exe = os.path.join(path, "Docker Desktop.exe")
    if os.path.exists(exe):
        return {"installed": True, "path": path, "exe": exe, "bin": os.path.join(path, "resources", "bin")}
    return {"installed": False, "path": "", "exe": "", "bin": ""}

def detect_wsl():
    wsl_exe = r"C:\Windows\System32\wsl.exe"
    if os.path.exists(wsl_exe):
        try:
            res = subprocess.run([wsl_exe, "-l", "-v"], capture_output=True, text=True, timeout=10)
            has_ubuntu = "Ubuntu-24.04" in res.stdout or "Ubuntu-24.04" in res.stderr
            return {"installed": True, "has_ubuntu24": has_ubuntu}
        except Exception:
            return {"installed": True, "has_ubuntu24": False}
    return {"installed": False, "has_ubuntu24": False}

def main():
    elevate()
    os.system("cls")
    print("=" * 70)
    print("     교육장 PC 원클릭 개발 환경 구축 솔루션 (멱등성 & 예외처리 강화)   ")
    print("=" * 70)
    
    pre_flight_checks()
    
    base_dir = Path(__file__).resolve().parent
    installers_dir = base_dir / "installers"
    installers_dir.mkdir(exist_ok=True)
    summary = []

    # [1/4] Python
    print("\n[1/4] Python 점검 및 환경 구성...")
    py = detect_python()
    if py["installed"]:
        print(f"[-] 이미 설치된 Python 감지: {py['version']} ({py['path']}) -> 설치 건너뜀")
        py_target_dir = py["path"]
        py_status = f"기존 설치 유지 ({py['version']})"
    else:
        print("[*] Python 신규 무인 설치 진행 중...")
        py_url = "https://www.python.org/ftp/python/3.12.8/python-3.12.8-amd64.exe"
        py_installer = installers_dir / "python-3.12.8-amd64.exe"
        py_target_dir = r"C:\Program Files\Python312"
        if download_file_with_retry(py_url, str(py_installer)):
            py_args = f'/quiet InstallAllUsers=1 PrependPath=1 Include_test=0 Include_pip=1 TargetDir="{py_target_dir}"'
            subprocess.run([str(py_installer), *py_args.split()], check=False)
            py_status = "신규 설치 완료 (3.12.8)"
        else:
            py_status = "설치 실패 (다운로드 오류)"

    if os.path.exists(py_target_dir):
        add_to_machine_path(py_target_dir)
        scripts_dir = os.path.join(py_target_dir, "Scripts")
        if os.path.exists(scripts_dir):
            add_to_machine_path(scripts_dir)
        create_shortcut("Python (명령줄)", os.path.join(py_target_dir, "python.exe"), "", os.path.join(py_target_dir, "python.exe,0"), "Python Shell")
        idle_path = os.path.join(py_target_dir, "Lib", "idlelib", "idle.pyw")
        if os.path.exists(idle_path):
            create_shortcut("IDLE (Python GUI)", os.path.join(py_target_dir, "pythonw.exe"), f'"{idle_path}"', os.path.join(py_target_dir, "python.exe,0"), "IDLE")
    summary.append(("Python (CMD: python)", py_status))

    # [2/4] VS Code
    print("\n[2/4] Visual Studio Code 및 한국어 언어팩 점검...")
    code = detect_vscode()
    code_target_dir = r"C:\Program Files\Microsoft VS Code"
    if code["installed"]:
        print(f"[-] 이미 설치된 VS Code 감지: {code['path']} -> 본체 설치 건너뜀")
        code_target_dir = code["path"]
        code_cmd = code["cmd"]
        code_status = "기존 설치 유지"
    else:
        print("[*] VS Code 신규 시스템 무인 설치 진행 중...")
        vscode_url = "https://update.code.visualstudio.com/latest/win32-x64/stable"
        vscode_installer = installers_dir / "VSCodeSetup-x64.exe"
        if download_file_with_retry(vscode_url, str(vscode_installer)):
            vscode_args = "/VERYSILENT /NORESTART /MERGETASKS=!runcode,addtopath,desktopicon,quicklaunchicon"
            subprocess.run([str(vscode_installer), *vscode_args.split()], check=False)
            code_cmd = os.path.join(code_target_dir, "bin", "code.cmd")
            code_status = "신규 설치 완료"
        else:
            code_cmd = ""
            code_status = "설치 실패 (다운로드 오류)"

    if code_cmd and os.path.exists(code_cmd):
        add_to_machine_path(os.path.dirname(code_cmd))
        if code.get("korean_ext", False):
            print("[-] 한국어 언어팩 이미 설치됨")
        else:
            print("[*] 한국어 언어팩 설치 중...")
            subprocess.run([code_cmd, "--install-extension", "MS-CEINTL.vscode-language-pack-ko", "--force"], check=False)
        try:
            appdata_code = Path(os.environ.get("APPDATA", "")) / "Code" / "User"
            appdata_code.mkdir(parents=True, exist_ok=True)
            argv_json = appdata_code / "argv.json"
            cfg = {"locale": "ko"}
            if argv_json.exists():
                try:
                    with open(argv_json, 'r', encoding='utf-8') as f:
                        cfg = json.load(f)
                    cfg["locale"] = "ko"
                except Exception:
                    pass
            with open(argv_json, 'w', encoding='utf-8') as f:
                json.dump(cfg, f, indent=4)
        except Exception:
            pass
        create_shortcut("Visual Studio Code", os.path.join(code_target_dir, "Code.exe"), "", os.path.join(code_target_dir, "Code.exe,0"), "VS Code")
    summary.append(("VS Code (한국어팩)", code_status))

    # [3/4] Docker Desktop
    print("\n[3/4] Docker Desktop 점검 및 설치...")
    docker = detect_docker()
    docker_target_dir = r"C:\Program Files\Docker\Docker"
    if docker["installed"]:
        print(f"[-] 이미 설치된 Docker Desktop 감지: {docker['path']} -> 설치 건너뜀")
        docker_status = "기존 설치 유지"
    else:
        print("[*] Docker Desktop 신규 무인 설치 진행 중...")
        docker_url = "https://desktop.docker.com/win/main/amd64/Docker%20Desktop%20Installer.exe"
        docker_installer = installers_dir / "DockerDesktopInstaller.exe"
        if download_file_with_retry(docker_url, str(docker_installer)):
            docker_args = ["install", "--quiet", "--accept-license", "--backend=wsl-2"]
            subprocess.run([str(docker_installer), *docker_args], check=False)
            docker_status = "신규 설치 완료"
        else:
            docker_status = "설치 실패 (다운로드 오류)"

    docker_bin = os.path.join(docker_target_dir, "resources", "bin")
    if os.path.exists(docker_bin):
        add_to_machine_path(docker_bin)
    docker_exe = os.path.join(docker_target_dir, "Docker Desktop.exe")
    if os.path.exists(docker_exe):
        create_shortcut("Docker Desktop", docker_exe, "", f"{docker_exe},0", "Docker Desktop")
    summary.append(("Docker Desktop", docker_status))

    # [4/4] WSL & Ubuntu 24.04
    print("\n[4/4] WSL2 및 Ubuntu 24.04 점검 및 설정...")
    subprocess.run(["dism.exe", "/online", "/enable-feature", "/featurename:Microsoft-Windows-Subsystem-Linux", "/all", "/norestart"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    subprocess.run(["dism.exe", "/online", "/enable-feature", "/featurename:VirtualMachinePlatform", "/all", "/norestart"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    subprocess.run(["wsl.exe", "--set-default-version", "2"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    
    wsl = detect_wsl()
    if wsl.get("has_ubuntu24", False):
        print("[-] Ubuntu 24.04 배포판 이미 등록됨 -> 설치 건너뜀")
        wsl_status = "기존 배포판 유지 (Ubuntu-24.04)"
    else:
        print("[*] Ubuntu 24.04 배포판 등록 진행 중...")
        subprocess.run(["wsl.exe", "--install", "-d", "Ubuntu-24.04", "--no-launch"], check=False)
        wsl_status = "신규 배포판 등록 완료"
        
    create_shortcut("Ubuntu 24.04 LTS", r"C:\Windows\System32\wsl.exe", "-d Ubuntu-24.04", r"C:\Windows\System32\wsl.exe,0", "Ubuntu 24.04 WSL")
    summary.append(("WSL2 Ubuntu 24.04", wsl_status))

    # 브로드캐스트
    try:
        HWND_BROADCAST = 0xFFFF
        WM_SETTINGCHANGE = 0x001A
        SMTO_ABORTIFHUNG = 0x0002
        result = ctypes.c_ulong()
        ctypes.windll.user32.SendMessageTimeoutW(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "Environment", SMTO_ABORTIFHUNG, 5000, ctypes.byref(result))
    except Exception:
        pass

    print("\n" + "=" * 70)
    print("                    최종 환경 구축 및 점검 결과                      ")
    print("=" * 70)
    for item, status in summary:
        print(f"  * {item:<28} : {status}")
    print("=" * 70)
    print("모든 작업이 안전하게 완료되었습니다.")
    print("새 CMD 창을 열거나 'verify_env.bat'를 실행하여 명령어를 검증하세요.")
    input("\n종료하려면 Enter 키를 누르세요...")

if __name__ == "__main__":
    main()
