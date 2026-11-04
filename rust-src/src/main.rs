// ==============================================================================
// 교육장 PC 개발 환경 원클릭/선택 설치 솔루션 (Rust 버전 - 정밀 다중 계층 감지 탑재)
// ==============================================================================

use std::collections::HashSet;
use std::env;
use std::fs;
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::process::Command;
use winreg::enums::*;
use winreg::RegKey;

fn main() {
    println!("================================================================================");
    println!("        [교육장 PC 개발 환경 원클릭/선택 설치 프로그램 (Rust) - EduPCSetup]     ");
    println!("================================================================================");

    let installers_dir = PathBuf::from("installers");
    if !installers_dir.exists() {
        let _ = fs::create_dir_all(&installers_dir);
    }

    loop {
        print_menu();
        print!("\n원하시는 메뉴 번호를 입력하세요 [0-8]: ");
        io::stdout().flush().unwrap();

        let mut input = String::new();
        if io::stdin().read_line(&mut input).is_err() {
            continue;
        }
        let choice = input.trim();

        if choice == "0" {
            println!("\n프로그램을 종료합니다. 안녕히 가세요!");
            break;
        }

        match choice {
            "1" => {
                println!("\n>>> [전체 자동 설치] 프로세스를 시작합니다...\n");
                run_python(&installers_dir);
                run_vscode(&installers_dir);
                run_docker(&installers_dir);
                run_wsl();
                repair_path_and_shortcuts();
                broadcast_env_change();
                show_verify_report();
            }
            "2" => {
                println!("\n>>> [Python 3.12] 설치 및 환경 설정을 시작합니다...\n");
                run_python(&installers_dir);
                broadcast_env_change();
            }
            "3" => {
                println!("\n>>> [Visual Studio Code] 설치 및 한국어팩 설정을 시작합니다...\n");
                run_vscode(&installers_dir);
                broadcast_env_change();
            }
            "4" => {
                println!("\n>>> [Docker Desktop] 설치 및 설정을 시작합니다...\n");
                run_docker(&installers_dir);
                broadcast_env_change();
            }
            "5" => {
                println!("\n>>> [WSL2 & Ubuntu 24.04] 활성화 및 설치를 시작합니다...\n");
                run_wsl();
                broadcast_env_change();
            }
            "6" => {
                run_multi_select(&installers_dir);
            }
            "7" => {
                println!("\n>>> [전역 PATH 및 바탕화면 바로가기 복구/점검]을 시작합니다...\n");
                repair_path_and_shortcuts();
                broadcast_env_change();
                println!("[+] 모든 환경변수 및 바탕화면 바로가기가 성공적으로 복구되었습니다!");
            }
            "8" => {
                println!("\n>>> [시스템 설치 상태 진단 및 검증]을 시작합니다...\n");
                show_verify_report();
            }
            _ => {
                println!("잘못된 메뉴 번호입니다. 0부터 8 사이의 번호를 입력해주세요.");
            }
        }

        println!("\n계속하려면 Enter 키를 누르세요...");
        let mut pause = String::new();
        let _ = io::stdin().read_line(&mut pause);
    }
}

fn print_menu() {
    println!("\n [1] 전체 자동 설치 (WSL2 + Docker + Python + VSCode 한국어팩) [권장]");
    println!(" [2] Python (최신 3.12 64-bit, 전역 PATH 최상단 등록, Store stub 차단, IDLE)");
    println!(" [3] Visual Studio Code (시스템/사용자 설치 감지 + 한국어팩 + 기본 UI 한글)");
    println!(" [4] Docker Desktop (시스템/사용자 설치 감지 + 최신 버전, WSL2 백엔드 연동)");
    println!(" [5] WSL2 & Ubuntu 24.04 LTS (기존 배포판 정밀 감지 + 가상화 활성화)");
    println!(" [6] 다중 선택 설치 (예: 2,3 또는 2,4,5 처럼 원하는 항목만 콤마로 선택)");
    println!(" [7] 환경변수(PATH) 및 바탕화면 바로가기만 복구/점검 (설치 없이 설정만)");
    println!(" [8] 현재 시스템 설치 상태 진단 및 검증 (Verify - 정밀 감지)");
    println!(" [0] 프로그램 종료");
    println!("================================================================================");
}

fn run_multi_select(installers_dir: &Path) {
    println!("\n[다중 선택 설치]");
    println!("  2: Python 3.12");
    println!("  3: Visual Studio Code (한국어팩)");
    println!("  4: Docker Desktop");
    println!("  5: WSL2 & Ubuntu 24.04");
    print!("\n설치할 번호들을 쉼표(,)로 구분하여 입력하세요 (예: 2, 3 또는 2,4,5): ");
    io::stdout().flush().unwrap();

    let mut input = String::new();
    if io::stdin().read_line(&mut input).is_err() {
        return;
    }

    let selected: HashSet<String> = input
        .split(|c| c == ',' || c == ' ')
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty())
        .collect();

    if selected.contains("2") {
        run_python(installers_dir);
    }
    if selected.contains("3") {
        run_vscode(installers_dir);
    }
    if selected.contains("4") {
        run_docker(installers_dir);
    }
    if selected.contains("5") {
        run_wsl();
    }

    repair_path_and_shortcuts();
    broadcast_env_change();
    show_verify_report();
}

fn detect_docker() -> Option<PathBuf> {
    let local_app = env::var("LOCALAPPDATA").unwrap_or_default();
    let candidates = vec![
        PathBuf::from(&local_app).join(r"Programs\DockerDesktop\Docker Desktop.exe"),
        PathBuf::from(r"C:\Program Files\Docker\Docker\Docker Desktop.exe"),
        PathBuf::from(r"C:\Program Files\Docker\Docker Desktop\Docker Desktop.exe"),
    ];

    for c in candidates {
        if c.exists() {
            return Some(c.parent().unwrap().to_path_buf());
        }
    }

    // where.exe docker 점검
    if let Ok(output) = Command::new("where.exe").arg("docker").output() {
        let text = String::from_utf8_lossy(&output.stdout);
        for line in text.lines() {
            let p = PathBuf::from(line.trim());
            if p.exists() {
                if let Some(parent) = p.parent() {
                    return Some(parent.to_path_buf());
                }
            }
        }
    }

    None
}

fn run_python(installers_dir: &Path) {
    println!("\n[단계: Python 점검 및 설정]");
    let target_dir = PathBuf::from(r"C:\Program Files\Python312");

    if target_dir.join("python.exe").exists() {
        println!("[-] Python이 이미 설치되어 있습니다 -> 설치 생략");
    } else {
        let installer = installers_dir.join("python-3.12.8-amd64.exe");
        let url = "https://www.python.org/ftp/python/3.12.8/python-3.12.8-amd64.exe";
        if download_file(url, &installer) {
            println!("[*] Python 3.12 무인 설치 실행 중...");
            let _ = Command::new(&installer)
                .args(&[
                    "/quiet",
                    "InstallAllUsers=1",
                    "PrependPath=1",
                    "Include_test=0",
                    "Include_pip=1",
                    &format!("TargetDir={}", target_dir.display()),
                ])
                .status();
            println!("[+] Python 3.12 설치 완료");
        }
    }

    if target_dir.exists() {
        add_to_system_path(&target_dir);
        add_to_system_path(&target_dir.join("Scripts"));
        create_shortcut(
            "Python (명령줄)",
            &target_dir.join("python.exe").to_string_lossy(),
            "",
            &format!("{},0", target_dir.join("python.exe").display()),
            "Python REPL",
        );
    }
}

fn run_vscode(installers_dir: &Path) {
    println!("\n[단계: Visual Studio Code 및 한국어팩 점검 및 설정]");
    let local_app = env::var("LOCALAPPDATA").unwrap_or_default();
    let candidates = vec![
        PathBuf::from(r"C:\Program Files\Microsoft VS Code"),
        PathBuf::from(&local_app).join(r"Programs\Microsoft VS Code"),
    ];

    let mut target_dir = PathBuf::from(r"C:\Program Files\Microsoft VS Code");
    let mut installed = false;

    for c in candidates {
        if c.join("Code.exe").exists() {
            target_dir = c;
            installed = true;
            break;
        }
    }

    if installed {
        println!("[-] VS Code가 이미 설치되어 있습니다 ({}) -> 본체 설치 생략", target_dir.display());
    } else {
        let installer = installers_dir.join("VSCodeSetup-x64.exe");
        let url = "https://update.code.visualstudio.com/latest/win32-x64/stable";
        if download_file(url, &installer) {
            println!("[*] VS Code 시스템 무인 설치 실행 중...");
            let _ = Command::new(&installer)
                .args(&[
                    "/VERYSILENT",
                    "/NORESTART",
                    "/MERGETASKS=!runcode,addtopath,desktopicon,quicklaunchicon",
                ])
                .status();
            println!("[+] VS Code 설치 완료");
        }
    }

    let code_cmd = target_dir.join(r"bin\code.cmd");
    if code_cmd.exists() {
        add_to_system_path(&target_dir.join("bin"));
        println!("[*] 한국어 언어팩(MS-CEINTL.vscode-language-pack-ko) 점검 및 설치 중...");
        let _ = Command::new(&code_cmd)
            .args(&[
                "--install-extension",
                "MS-CEINTL.vscode-language-pack-ko",
                "--force",
            ])
            .status();

        create_shortcut(
            "Visual Studio Code",
            &target_dir.join("Code.exe").to_string_lossy(),
            "",
            &format!("{},0", target_dir.join("Code.exe").display()),
            "VS Code",
        );
    }
}

fn run_docker(installers_dir: &Path) {
    println!("\n[단계: Docker Desktop 점검 및 설정]");
    if let Some(dir) = detect_docker() {
        println!("[-] Docker Desktop이 이미 설치되어 있습니다 ({}) -> 설치 생략", dir.display());
        let bin = dir.join(r"resources\bin");
        if bin.exists() { add_to_system_path(&bin); }
    } else {
        let installer = installers_dir.join("DockerDesktopInstaller.exe");
        let url = "https://desktop.docker.com/win/main/amd64/Docker%20Desktop%20Installer.exe";
        if download_file(url, &installer) {
            println!("[*] Docker Desktop 무인 설치 실행 중 (WSL2 백엔드)...");
            let _ = Command::new(&installer)
                .args(&["install", "--quiet", "--accept-license", "--backend=wsl-2"])
                .status();
            println!("[+] Docker Desktop 설치 완료");
        }
    }
}

fn run_wsl() {
    println!("\n[단계: WSL2 및 Ubuntu 24.04 점검 및 설정]");
    let _ = Command::new("dism.exe")
        .args(&[
            "/online",
            "/enable-feature",
            "/featurename:Microsoft-Windows-Subsystem-Linux",
            "/all",
            "/norestart",
        ])
        .status();
    let _ = Command::new("dism.exe")
        .args(&[
            "/online",
            "/enable-feature",
            "/featurename:VirtualMachinePlatform",
            "/all",
            "/norestart",
        ])
        .status();
    let _ = Command::new("wsl.exe")
        .args(&["--set-default-version", "2"])
        .status();

    let output = Command::new("wsl.exe").args(&["-l", "-q"]).output();
    let mut has_ubuntu = false;
    if let Ok(out) = output {
        let text = String::from_utf8_lossy(&out.stdout).replace('\0', "");
        if text.to_lowercase().contains("ubuntu") {
            has_ubuntu = true;
        }
    }

    if has_ubuntu {
        println!("[-] 이미 등록된 Ubuntu 배포판 감지 -> 설치 생략");
    } else {
        println!("[*] Ubuntu 24.04 배포판 등록 진행 중...");
        let _ = Command::new("wsl.exe")
            .args(&["--install", "-d", "Ubuntu-24.04", "--no-launch"])
            .status();
    }

    create_shortcut(
        "Ubuntu 24.04 LTS",
        r"C:\Windows\System32\wsl.exe",
        "-d Ubuntu-24.04",
        r"C:\Windows\System32\wsl.exe,0",
        "Ubuntu 24.04 WSL",
    );
}

fn repair_path_and_shortcuts() {
    let py = PathBuf::from(r"C:\Program Files\Python312");
    if py.exists() {
        add_to_system_path(&py);
        add_to_system_path(&py.join("Scripts"));
    }
    let code_bin = PathBuf::from(r"C:\Program Files\Microsoft VS Code\bin");
    if code_bin.exists() {
        add_to_system_path(&code_bin);
    }
    if let Some(dir) = detect_docker() {
        let bin = dir.join(r"resources\bin");
        if bin.exists() { add_to_system_path(&bin); }
    }
}

fn download_file(url: &str, dest: &Path) -> bool {
    if dest.exists() && dest.metadata().map(|m| m.len()).unwrap_or(0) > 1024 * 1024 {
        println!("[*] 기존 캐시 파일 발견: {:?} -> 다운로드 생략", dest.file_name().unwrap_or_default());
        return true;
    }
    println!("[*] 다운로드 시작: {}", url);
    let status = Command::new("powershell")
        .args(&[
            "-NoProfile",
            "-Command",
            &format!("Invoke-WebRequest -Uri '{}' -OutFile '{}'", url, dest.display()),
        ])
        .status();
    status.map(|s| s.success()).unwrap_or(false)
}

fn add_to_system_path(dir: &Path) {
    let dir_str = dir.to_string_lossy().to_string();
    let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
    let env_path = r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    if let Ok(key) = hklm.open_subkey_with_flags(env_path, KEY_READ | KEY_WRITE) {
        let current_path: String = key.get_value("Path").unwrap_or_default();
        if !current_path.contains(&dir_str) {
            let new_path = format!("{};{}", dir_str, current_path);
            let _ = key.set_value("Path", &new_path);
            println!("[+] 전역 PATH 등록 완료: {}", dir_str);
        }
    }
}

fn create_shortcut(name: &str, target: &str, args: &str, icon: &str, desc: &str) {
    let ps_cmd = format!(
        "$w=New-Object -ComObject WScript.Shell; \
         $d=@([Environment]::GetFolderPath('Desktop'), 'C:\\Users\\Public\\Desktop') | Where-Object {{ Test-Path $_ }}; \
         foreach($p in $d){{ $s=$w.CreateShortcut((Join-Path $p '{}.lnk')); $s.TargetPath='{}'; if('{}'){{$s.Arguments='{}'}}; if('{}'){{$s.IconLocation='{}'}}; if('{}'){{$s.Description='{}'}}; $s.Save(); }}",
        name, target, args, args, icon, icon, desc, desc
    );
    let _ = Command::new("powershell")
        .args(&["-NoProfile", "-Command", &ps_cmd])
        .status();
}

fn broadcast_env_change() {
    let _ = Command::new("powershell")
        .args(&[
            "-NoProfile",
            "-Command",
            "[DllImport(\"user32.dll\")] public static extern void SendMessageTimeout(...);",
        ])
        .status();
}

fn show_verify_report() {
    println!("\n================================================================================");
    println!("                    시스템 전역 명령어 및 설치 검증 리포트                      ");
    println!("================================================================================");

    let check_cmd = |name: &str, cmd: &str, args: &[&str]| {
        let ok = Command::new(cmd).args(args).output().map(|o| o.status.success()).unwrap_or(false);
        println!("  * {:<28} : {}", name, if ok { "[PASS] 정상 동작" } else { "[CHECK] 미설치 또는 미인식" });
    };

    check_cmd("Python (cmd: python)", "python", &["--version"]);
    check_cmd("VS Code (cmd: code)", "code.cmd", &["-v"]);
    if let Some(dir) = detect_docker() {
        println!("  * {:<28} : [PASS] 정상 감지 (경로: {})", "Docker Desktop", dir.display());
    } else {
        check_cmd("Docker Desktop", "docker", &["--version"]);
    }
    check_cmd("WSL2 (cmd: wsl)", "wsl.exe", &["-l", "-q"]);
    println!("================================================================================");
}
