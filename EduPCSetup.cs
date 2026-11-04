using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace OnePunchSetup
{
    class Program
    {
        private static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string InstallersDir = Path.Combine(AppDir, "installers");

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam,
            uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

        [DllImport("kernel32.dll")]
        public static extern bool IsProcessorFeaturePresent(int ProcessorFeature);

        static void SafeClear()
        {
            try { Console.Clear(); } catch { }
        }

        static void SafeReadKey()
        {
            try { Console.ReadKey(true); } catch { }
        }

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            try { Console.Title = "원펀치 (OnePunch) v3.0"; } catch { }
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768; // Tls12 | Tls11

            bool skipAdmin = false;
            string cliChoice = null;
            foreach (var arg in args)
            {
                if (arg.Equals("/noadmin", StringComparison.OrdinalIgnoreCase) || arg.Equals("--no-admin", StringComparison.OrdinalIgnoreCase))
                {
                    skipAdmin = true;
                }
                else if (arg.Equals("/check", StringComparison.OrdinalIgnoreCase) || arg.Equals("--check", StringComparison.OrdinalIgnoreCase))
                {
                    cliChoice = "12";
                }
                else if (arg.Equals("/all", StringComparison.OrdinalIgnoreCase) || arg.Equals("--all", StringComparison.OrdinalIgnoreCase))
                {
                    cliChoice = "1";
                }
                else
                {
                    int n;
                    if (int.TryParse(arg, out n) && n >= 0 && n <= 12)
                    {
                        cliChoice = arg;
                    }
                }
            }

            if (!skipAdmin && !IsAdministrator())
            {
                Console.WriteLine("[안내] 관리자 권한이 필요합니다. 관리자 권한(UAC)으로 재실행합니다...");
                RestartAsAdmin();
                return;
            }

            if (!Directory.Exists(InstallersDir))
            {
                Directory.CreateDirectory(InstallersDir);
            }

            // 명령줄 인수로 번호나 옵션이 전달된 경우 즉시 실행 후 종료
            if (cliChoice != null)
            {
                RunChoice(cliChoice);
                return;
            }

            while (true)
            {
                SafeClear();
                PrintBanner();
                PrintMenu();

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("\n선택 [0-12]: ");
                Console.ResetColor();

                string choice = Console.ReadLine();
                if (choice != null) choice = choice.Trim();
                if (string.IsNullOrEmpty(choice)) continue;

                if (choice == "0")
                {
                    Console.WriteLine("\n원펀치를 종료합니다.");
                    break;
                }

                RunChoice(choice);

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("\n계속하려면 아무 키나 누르세요...");
                Console.ResetColor();
                SafeReadKey();
            }
        }

        static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        static void RestartAsAdmin()
        {
            try
            {
                ProcessStartInfo proc = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = Process.GetCurrentProcess().MainModule.FileName,
                    Verb = "runas"
                };
                Process.Start(proc);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("관리자 권한 승격 실패: " + ex.Message);
                Console.ResetColor();
                Console.ReadKey();
            }
        }

        static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("================================================================================");
            Console.WriteLine("                            [원펀치 (OnePunch) v3.0]                            ");
            Console.WriteLine("================================================================================");
            Console.ResetColor();
            Console.WriteLine(" * 설치 엔진: 공식 고속 무인 인스톨러 (Direct Official Silent Engine)");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" * 실습실 PC는 공용이므로 퇴실 전 반드시 GitHub에 'git push' 하세요.");
            Console.ResetColor();
            Console.WriteLine("--------------------------------------------------------------------------------");
        }

        static void PrintMenu()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(" [1] 전체 설치");
            Console.ResetColor();
            Console.WriteLine(" [2] Git for Windows");
            Console.WriteLine(" [3] Python 3.12");
            Console.WriteLine(" [4] Visual Studio Code");
            Console.WriteLine(" [5] Docker Desktop");
            Console.WriteLine(" [6] WSL2 & Ubuntu 24.04 LTS");
            Console.WriteLine(" [7] Discord");
            Console.WriteLine(" [8] WSL 리눅스 개발 환경 설정");
            Console.WriteLine(" [9] 터미널 파워 도구 팩");
            Console.WriteLine(" [10] 다중 선택 설치");
            Console.WriteLine(" [11] 환경변수 및 바로가기 복구");
            Console.WriteLine(" [12] 설치 상태 검증");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(" [0] 종료");
            Console.ResetColor();
            Console.WriteLine("================================================================================");
        }

        static void RunChoice(string choice)
        {
            SafeClear();
            PrintBanner();

            switch (choice)
            {
                case "1":
                    RunFullInstallationPipeline();
                    break;

                case "2":
                    Console.WriteLine("\n>>> [2. Git for Windows] 시작\n");
                    ExecuteGit();
                    BroadcastEnvChange();
                    break;

                case "3":
                    Console.WriteLine("\n>>> [3. Python 3.12] 시작\n");
                    ExecutePython();
                    BroadcastEnvChange();
                    break;

                case "4":
                    Console.WriteLine("\n>>> [4. Visual Studio Code] 시작\n");
                    ExecuteVSCode();
                    BroadcastEnvChange();
                    break;

                case "5":
                    Console.WriteLine("\n>>> [5. Docker Desktop] 시작\n");
                    ExecuteDocker();
                    BroadcastEnvChange();
                    break;

                case "6":
                    Console.WriteLine("\n>>> [6. WSL2 & Ubuntu 24.04 LTS] 시작\n");
                    ExecuteWSL();
                    BroadcastEnvChange();
                    break;

                case "7":
                    Console.WriteLine("\n>>> [7. Discord] 시작\n");
                    ExecuteDiscord();
                    break;

                case "8":
                    Console.WriteLine("\n>>> [8. WSL 리눅스 개발 환경 설정] 시작\n");
                    ProvisionWSLLinux("");
                    break;

                case "9":
                    Console.WriteLine("\n>>> [9. 터미널 파워 도구 팩] 시작\n");
                    InstallTerminalPowerTools("");
                    break;

                case "10":
                    RunMultiSelect();
                    break;

                case "11":
                    Console.WriteLine("\n>>> [11. 환경변수 및 바로가기 복구] 시작\n");
                    RepairEnvironmentAndShortcuts();
                    BroadcastEnvChange();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n[+] 환경변수 및 바로가기 복구 완료");
                    Console.ResetColor();
                    break;

                case "12":
                    Console.WriteLine("\n>>> [12. 설치 상태 검증] 시작\n");
                    ShowVerifyReport();
                    break;

                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("잘못된 번호입니다. [0-12] 사이 숫자를 입력하세요.");
                    Console.ResetColor();
                    break;
            }
        }

        static void RunFullInstallationPipeline()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n================================================================================");
            Console.WriteLine("                   [원펀치 v3.0 올인원 전체 자동 설치 파이프라인]               ");
            Console.WriteLine("================================================================================");
            Console.ResetColor();

            // 0. 사전 진단
            Console.WriteLine("\n[단계 0/7] 시스템 환경 및 하드웨어 가상화 진단");
            Console.WriteLine("--------------------------------------------------------------------------------");
            PerformPreFlightChecks();

            // 1. Git for Windows
            Console.WriteLine("\n[단계 1/7] Git for Windows 설치 및 환경변수 등록");
            Console.WriteLine("--------------------------------------------------------------------------------");
            ExecuteGit();
            var git = DetectGit();
            PrintStepResult("Git for Windows", git.Installed, git.Version);

            // 2. Python 3.12
            Console.WriteLine("\n[단계 2/7] Python 3.12 설치 및 환경변수 등록");
            Console.WriteLine("--------------------------------------------------------------------------------");
            ExecutePython();
            var py = DetectPython();
            PrintStepResult("Python 3.12", py.Installed, py.Version);

            // 3. Visual Studio Code & 필수 확장팩
            Console.WriteLine("\n[단계 3/7] Visual Studio Code 및 필수 확장팩 구성");
            Console.WriteLine("--------------------------------------------------------------------------------");
            ExecuteVSCode();
            var code = DetectVSCode();
            PrintStepResult("Visual Studio Code", code.Installed, code.KoreanExt ? "한국어팩 적용됨" : "기본 설치됨");

            // 4. WSL2 & Ubuntu 24.04 LTS (Docker 설치 전 필수 선행)
            Console.WriteLine("\n[단계 4/7] WSL2 및 Ubuntu 24.04 LTS 가상화 환경 구성");
            Console.WriteLine("--------------------------------------------------------------------------------");
            ExecuteWSL();
            var wsl = DetectWSL();
            PrintStepResult("WSL2 / Ubuntu", wsl.Installed, wsl.HasUbuntu ? (wsl.DistroName + " 등록됨") : "WSL2 등록 대기 (재부팅 후 활성화)");

            // 5. Docker Desktop 설치 및 WSL2 연동
            Console.WriteLine("\n[단계 5/7] Docker Desktop 설치 및 WSL2 백엔드 연동");
            Console.WriteLine("--------------------------------------------------------------------------------");
            ExecuteDocker();
            var docker = DetectDocker();
            var dockerWsl = DetectDockerWSLIntegration();
            PrintStepResult("Docker Desktop & WSL2", docker.Installed, dockerWsl.Configured ? "WSL2 통합 연동 완료" : "연동 설정 완료");

            // 6. WSL 리눅스 내부 개발 환경 & 터미널 파워 도구
            Console.WriteLine("\n[단계 6/7] WSL 리눅스 개발 환경 및 터미널 파워 도구 팩");
            Console.WriteLine("--------------------------------------------------------------------------------");
            ProvisionWSLLinux("");
            InstallTerminalPowerTools("");

            // 7. Discord 및 환경 정리
            Console.WriteLine("\n[단계 7/7] Discord 설치 및 바로가기/환경변수 최종 정리");
            Console.WriteLine("--------------------------------------------------------------------------------");
            ExecuteDiscord();
            RepairEnvironmentAndShortcuts();
            BroadcastEnvChange();

            // 최종 종합 리포트 및 재부팅 안내
            ShowVerifyReport();
            PromptPostInstallReboot();
        }

        static void PrintStepResult(string name, bool success, string detail)
        {
            Console.ForegroundColor = success ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine(string.Format("==> [{0}] {1}: {2}", success ? "성공" : "확인필요", name, detail));
            Console.ResetColor();
        }

        static void RunMultiSelect()
        {
            Console.WriteLine("\n[다중 선택 설치]");
            Console.WriteLine("  2: Git for Windows");
            Console.WriteLine("  3: Python 3.12");
            Console.WriteLine("  4: Visual Studio Code");
            Console.WriteLine("  5: Docker Desktop");
            Console.WriteLine("  6: WSL2 & Ubuntu 24.04 LTS");
            Console.WriteLine("  7: Discord");
            Console.WriteLine("  8: WSL 리눅스 개발 환경 설정");
            Console.WriteLine("  9: 터미널 파워 도구 팩");
            Console.Write("\n설치할 번호를 쉼표로 입력 (예: 2,3,7 또는 4,5,6): ");
            string input = Console.ReadLine();
            if (string.IsNullOrEmpty(input)) return;

            string[] tokens = input.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            HashSet<string> selected = new HashSet<string>(tokens);

            PerformPreFlightChecks();

            if (selected.Contains("2")) ExecuteGit();
            if (selected.Contains("3")) ExecutePython();
            if (selected.Contains("4")) ExecuteVSCode();
            if (selected.Contains("6")) ExecuteWSL();
            if (selected.Contains("5")) ExecuteDocker();
            if (selected.Contains("7")) ExecuteDiscord();
            if (selected.Contains("8")) ProvisionWSLLinux("");
            if (selected.Contains("9")) InstallTerminalPowerTools("");

            RepairEnvironmentAndShortcuts();
            BroadcastEnvChange();
            ShowVerifyReport();

            if (selected.Contains("5") || selected.Contains("6"))
            {
                PromptPostInstallReboot();
            }
        }

        static void PerformPreFlightChecks()
        {
            try
            {
                DriveInfo cDrive = new DriveInfo("C");
                long freeGb = cDrive.AvailableFreeSpace / (1024 * 1024 * 1024);
                if (freeGb < 15)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[경고] C: 드라이브 여유 공간 부족 (" + freeGb + " GB / 권장: 15GB 이상)");
                    Console.ResetColor();
                }
            }
            catch { }

            try
            {
                bool virtEnabled = IsProcessorFeaturePresent(21);
                if (!virtEnabled)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[주의] CPU 하드웨어 가상화(VT-x / AMD SVM)가 BIOS에서 꺼져 있을 수 있습니다.");
                    Console.WriteLine("       (WSL2/Docker 사용을 위해 부팅 시 BIOS에서 Virtualization을 활성화해야 합니다)");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[+] CPU 하드웨어 가상화(VT-x / AMD SVM) 지원 확인됨");
                    Console.ResetColor();
                }
            }
            catch { }

            string[] procs = { "git", "Code", "Docker Desktop", "python", "Discord" };
            foreach (var p in procs)
            {
                if (Process.GetProcessesByName(p).Length > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[알림] '" + p + "' 프로세스 실행 중 (종료 권장)");
                    Console.ResetColor();
                }
            }
        }

        #region Installation Logic

        static void ExecuteGit()
        {
            Console.WriteLine("\n[설치] Git for Windows 점검 및 공식 설치");

            var detected = DetectGit();
            string gitCmdDir = @"C:\Program Files\Git\cmd";

            if (detected.Installed)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("[-] 이미 설치됨: Git " + detected.Version + " (" + detected.Path + ")");
                Console.ResetColor();
                if (!string.IsNullOrEmpty(detected.Path)) gitCmdDir = detected.Path;
            }
            else
            {
                Console.WriteLine("[*] 공식 Git 인스톨러 다운로드 및 무인 설치 중...");
                string gitVersion = "2.47.1";
                string url = "https://github.com/git-for-windows/git/releases/download/v2.47.1.windows.1/Git-2.47.1-64-bit.exe";
                string installer = Path.Combine(InstallersDir, "Git-" + gitVersion + "-64-bit.exe");

                if (DownloadFileWithRetry(url, installer))
                {
                    string args = "/VERYSILENT /NORESTART /NOCANCEL /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS";
                    RunProcess(installer, args, true, 180000);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[+] Git for Windows 설치 완료");
                    Console.ResetColor();
                }

                var fresh = DetectGit();
                if (fresh.Installed && !string.IsNullOrEmpty(fresh.Path)) gitCmdDir = fresh.Path;
            }

            if (Directory.Exists(gitCmdDir))
            {
                AddToSystemPath(gitCmdDir);
            }
        }

        static void ExecutePython()
        {
            Console.WriteLine("\n[설치] Python 3.12 점검 및 공식 설치");

            var detected = DetectPython();
            string targetDir = @"C:\Program Files\Python312";

            if (detected.Installed)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("[-] 이미 설치됨: Python " + detected.Version + " (" + detected.Path + ")");
                Console.ResetColor();
                targetDir = detected.Path;
            }
            else
            {
                Console.WriteLine("[*] 공식 Python 인스톨러 다운로드 및 무인 설치 중...");
                string pyVersion = "3.12.8";
                string url = "https://www.python.org/ftp/python/" + pyVersion + "/python-" + pyVersion + "-amd64.exe";
                string installer = Path.Combine(InstallersDir, "python-" + pyVersion + "-amd64.exe");

                if (DownloadFileWithRetry(url, installer))
                {
                    string args = string.Format("/quiet InstallAllUsers=1 PrependPath=1 Include_test=0 Include_pip=1 TargetDir=\"{0}\"", targetDir);
                    RunProcess(installer, args, true, 180000);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[+] Python 3.12 설치 완료");
                    Console.ResetColor();
                }

                var fresh = DetectPython();
                if (fresh.Installed) targetDir = fresh.Path;
            }

            if (Directory.Exists(targetDir))
            {
                AddToSystemPath(targetDir);
                string scripts = Path.Combine(targetDir, "Scripts");
                if (Directory.Exists(scripts)) AddToSystemPath(scripts);

                FixPythonStoreAlias(targetDir);

                string pyExe = Path.Combine(targetDir, "python.exe");
                CreateShortcut("Python (명령줄)", pyExe, "", pyExe + ",0", "Python REPL");

                string idleScript = Path.Combine(targetDir, @"Lib\idlelib\idle.pyw");
                string pywExe = Path.Combine(targetDir, "pythonw.exe");
                if (File.Exists(pywExe) && File.Exists(idleScript))
                {
                    CreateShortcut("IDLE (Python GUI)", pywExe, "\"" + idleScript + "\"", pyExe + ",0", "Python IDLE");
                }
            }
        }

        static void ExecuteVSCode()
        {
            Console.WriteLine("\n[설치] Visual Studio Code 점검 및 공식 설치");

            var detected = DetectVSCode();
            string targetDir = @"C:\Program Files\Microsoft VS Code";
            string codeCmd = "";

            if (detected.Installed)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("[-] 이미 설치됨: VS Code (" + detected.Path + ")");
                Console.ResetColor();
                targetDir = detected.Path;
                codeCmd = detected.CodeCmd;
            }
            else
            {
                Console.WriteLine("[*] 공식 VS Code 인스톨러 다운로드 및 무인 설치 중...");
                string url = "https://update.code.visualstudio.com/latest/win32-x64/stable";
                string installer = Path.Combine(InstallersDir, "VSCodeSetup-x64.exe");

                if (DownloadFileWithRetry(url, installer))
                {
                    string args = "/VERYSILENT /NORESTART /MERGETASKS=!runcode,addtopath,desktopicon,quicklaunchicon";
                    RunProcess(installer, args, true, 180000);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[+] VS Code 설치 완료");
                    Console.ResetColor();
                }

                var fresh = DetectVSCode();
                if (fresh.Installed)
                {
                    targetDir = fresh.Path;
                    codeCmd = fresh.CodeCmd;
                }
            }

            if (File.Exists(codeCmd))
            {
                AddToSystemPath(Path.GetDirectoryName(codeCmd));

                Console.WriteLine("[*] 필수 확장팩 설치 중 (WSL, Python, GitLens, 한국어팩)...");
                string[] extensions = {
                    "MS-CEINTL.vscode-language-pack-ko",
                    "ms-vscode-remote.remote-wsl",
                    "ms-python.python",
                    "eamodio.gitlens",
                    "github.vscode-pull-request-github"
                };

                foreach (var ext in extensions)
                {
                    RunProcess(codeCmd, "--install-extension " + ext + " --force", true);
                }
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[+] VS Code 확장팩 설치 완료");
                Console.ResetColor();

                ConfigureVSCodeLocaleKo();

                string codeExe = Path.Combine(targetDir, "Code.exe");
                if (File.Exists(codeExe))
                {
                    CreateShortcut("Visual Studio Code", codeExe, "", codeExe + ",0", "VS Code");
                }
            }
        }

        static void ExecuteDocker()
        {
            Console.WriteLine("\n[설치] Docker Desktop 점검 및 공식 설치");

            var detected = DetectDocker();
            string targetDir = @"C:\Program Files\Docker\Docker";

            if (detected.Installed)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("[-] 이미 설치됨: Docker Desktop (" + detected.Path + ")");
                Console.ResetColor();
                targetDir = detected.Path;
            }
            else
            {
                Console.WriteLine("[*] 공식 Docker Desktop 인스톨러 다운로드 및 무인 설치 중 (WSL2 백엔드)...");
                string url = "https://desktop.docker.com/win/main/amd64/Docker%20Desktop%20Installer.exe";
                string installer = Path.Combine(InstallersDir, "DockerDesktopInstaller.exe");

                if (DownloadFileWithRetry(url, installer))
                {
                    string args = "install --quiet --accept-license --backend=wsl-2";
                    RunProcess(installer, args, true, 300000);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[+] Docker Desktop 설치 완료");
                    Console.ResetColor();
                }

                var fresh = DetectDocker();
                if (fresh.Installed) targetDir = fresh.Path;
            }

            string binDir = !string.IsNullOrEmpty(detected.BinPath) && Directory.Exists(detected.BinPath)
                ? detected.BinPath
                : Path.Combine(targetDir, @"resources\bin");
            if (Directory.Exists(binDir)) AddToSystemPath(binDir);

            string dockerExe = !string.IsNullOrEmpty(detected.Exe) && File.Exists(detected.Exe)
                ? detected.Exe
                : Path.Combine(targetDir, "Docker Desktop.exe");
            if (File.Exists(dockerExe))
            {
                CreateShortcut("Docker Desktop", dockerExe, "", dockerExe + ",0", "Docker Desktop");
            }

            FixWSLDockerPermission();
        }

        static void ExecuteWSL()
        {
            Console.WriteLine("\n[설치] WSL2 & Ubuntu 24.04 LTS 점검 및 등록");

            try
            {
                Console.WriteLine("[*] Windows 가상화 및 하이퍼바이저 필수 기능 구성 중...");
                RunProcess("dism.exe", "/online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart", true, 60000);
                RunProcess("dism.exe", "/online /enable-feature /featurename:VirtualMachinePlatform /all /norestart", true, 60000);
                RunProcess("dism.exe", "/online /enable-feature /featurename:HypervisorPlatform /all /norestart", true, 60000);
                RunProcess("bcdedit.exe", "/set hypervisorlaunchtype auto", true, 10000);
                RunProcess("wsl.exe", "--set-default-version 2", true, 30000);
            }
            catch { }

            var detected = DetectWSL();
            if (detected.HasUbuntu)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("[-] 이미 등록됨: " + detected.DistroName);
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine("[*] Ubuntu 24.04 LTS 등록 중 (wsl --install -d Ubuntu-24.04 --no-launch)...");
                int code = RunProcess("wsl.exe", "--install -d Ubuntu-24.04 --no-launch", true, 300000);
                if (code == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[+] Ubuntu 24.04 배포판 등록 완료");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[!] 배포판 초기 등록 완료 (가상화 기능 반영을 위해 PC 재부팅이 필요합니다)");
                    Console.ResetColor();
                }
            }

            CreateRobustWSLShortcut(detected.HasUbuntu ? detected.DistroName : "Ubuntu-24.04");
            ConfigureDockerWSLIntegration();
        }

        static void ExecuteDiscord()
        {
            Console.WriteLine("\n[설치] Discord 점검 및 공식 설치");

            var detected = DetectDiscord();

            if (detected.Installed)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("[-] 이미 설치됨: Discord " + detected.Version + " (" + detected.Exe + ")");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine("[*] 공식 Discord 인스톨러 다운로드 및 무인 설치 중...");
                string url = "https://dl.discordapp.net/distro/app/stable/win/x86/DiscordSetup.exe";
                string installer = Path.Combine(InstallersDir, "DiscordSetup.exe");

                if (DownloadFileWithRetry(url, installer))
                {
                    RunProcess(installer, "-s", true, 180000);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[+] Discord 설치 완료");
                    Console.ResetColor();
                }
            }

            var fresh = DetectDiscord();
            if (fresh.Installed && File.Exists(fresh.Exe))
            {
                CreateShortcut("Discord", fresh.Exe, "", fresh.Exe + ",0", "Discord");
            }
        }

        static void ProvisionWSLLinux(string distroName)
        {
            Console.WriteLine("\n[설정] WSL 리눅스 내부 개발 환경 설정");

            var wsl = DetectWSL();
            if (!wsl.Installed || !wsl.HasUbuntu)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[-] WSL 또는 Ubuntu 배포판이 아직 준비되지 않았습니다.");
                Console.WriteLine("    (PC 재부팅 후 배포판이 등록되면 [8]번 메뉴로 개별 실행할 수 있습니다)");
                Console.ResetColor();
                return;
            }

            string distro = string.IsNullOrEmpty(distroName) ? (wsl.HasUbuntu ? wsl.DistroName : "Ubuntu-24.04") : distroName;
            Console.WriteLine("[*] 대상 배포판: " + distro);

            Console.WriteLine("[*] 패키지 설치 중 (build-essential, git, curl, venv, ruff, gh)...");
            string aptCmd = "apt-get update && apt-get install -y build-essential curl wget git unzip python3-pip python3-venv gh jq";
            RunProcess("wsl.exe", string.Format("-d {0} -u root -- bash -c \"{1}\"", distro, aptCmd), true, 180000);

            string ruffCmd = "python3 -m pip install --break-system-packages ruff 2>/dev/null || pip install ruff 2>/dev/null || true";
            RunProcess("wsl.exe", string.Format("-d {0} -u root -- bash -c \"{1}\"", distro, ruffCmd), true, 60000);

            Console.WriteLine("[*] 작업 디렉터리 (~/projects) 생성 중...");
            string projectsCmd = "id -un 1000 2>/dev/null | xargs -I {} bash -c 'mkdir -p /home/{}/projects && chown -R {}:{} /home/{}/projects'";
            RunProcess("wsl.exe", string.Format("-d {0} -u root -- bash -c \"{1}\"", distro, projectsCmd), true, 30000);

            RunProcess("wsl.exe", string.Format("-d {0} -u root -- bash -c \"git config --system init.defaultBranch main\"", distro), true, 20000);

            FixWSLDockerPermission();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[+] WSL 리눅스 내부 개발 환경 설정 완료");
            Console.ResetColor();
        }

        static void InstallTerminalPowerTools(string distroName)
        {
            Console.WriteLine("\n[설정] 터미널 파워 도구 팩 설치");

            var wsl = DetectWSL();
            if (!wsl.Installed || !wsl.HasUbuntu)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[-] WSL 또는 Ubuntu 배포판이 아직 준비되지 않았습니다.");
                Console.WriteLine("    (PC 재부팅 후 배포판이 등록되면 [9]번 메뉴로 개별 실행할 수 있습니다)");
                Console.ResetColor();
                return;
            }

            string distro = string.IsNullOrEmpty(distroName) ? (wsl.HasUbuntu ? wsl.DistroName : "Ubuntu-24.04") : distroName;
            Console.WriteLine("[*] 도구 설치 중 (btop, tmux, ripgrep, fzf, zoxide, tealdeer)...");
            string cmd = "apt-get update && apt-get install -y btop tmux ripgrep fzf zoxide tealdeer && tldr --update 2>/dev/null || true";
            RunProcess("wsl.exe", string.Format("-d {0} -u root -- bash -c \"{1}\"", distro, cmd), true, 180000);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[+] 터미널 파워 도구 팩 설치 완료");
            Console.ResetColor();
        }

        static void ConfigureDockerWSLIntegration()
        {
            try
            {
                var wsl = DetectWSL();
                string distro = wsl.HasUbuntu ? wsl.DistroName : "Ubuntu-24.04";

                // 1. Windows 호스트 Docker Desktop settings.json에 WSL2 통합 옵션 자동 반영
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dockerDir = Path.Combine(appData, "Docker");
                if (!Directory.Exists(dockerDir)) Directory.CreateDirectory(dockerDir);

                string settingsFile = Path.Combine(dockerDir, "settings.json");
                if (!File.Exists(settingsFile))
                {
                    string initialJson = "{\n" +
                                         "  \"wslEngineEnabled\": true,\n" +
                                         "  \"enableIntegrationWithDefaultWslDistro\": true,\n" +
                                         "  \"integratedWslDistros\": [\n" +
                                         "    \"" + distro + "\",\n" +
                                         "    \"Ubuntu-24.04\",\n" +
                                         "    \"Ubuntu\"\n" +
                                         "  ]\n" +
                                         "}";
                    File.WriteAllText(settingsFile, initialJson, Encoding.UTF8);
                    Console.WriteLine("[+] Docker Desktop WSL2 연동 설정 파일 생성 완료 (" + distro + ")");
                }
                else
                {
                    string json = File.ReadAllText(settingsFile);
                    bool modified = false;

                    if (json.IndexOf("\"wslEngineEnabled\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (json.IndexOf("\"wslEngineEnabled\": false", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            json = json.Replace("\"wslEngineEnabled\": false", "\"wslEngineEnabled\": true");
                            modified = true;
                        }
                        else if (json.IndexOf("\"wslEngineEnabled\":false", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            json = json.Replace("\"wslEngineEnabled\":false", "\"wslEngineEnabled\":true");
                            modified = true;
                        }
                    }
                    else
                    {
                        int firstBrace = json.IndexOf('{');
                        if (firstBrace >= 0)
                        {
                            json = json.Insert(firstBrace + 1, "\n  \"wslEngineEnabled\": true,\n  \"enableIntegrationWithDefaultWslDistro\": true,");
                            modified = true;
                        }
                    }

                    if (json.IndexOf("\"enableIntegrationWithDefaultWslDistro\": false", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        json = json.Replace("\"enableIntegrationWithDefaultWslDistro\": false", "\"enableIntegrationWithDefaultWslDistro\": true");
                        modified = true;
                    }
                    else if (json.IndexOf("\"enableIntegrationWithDefaultWslDistro\":false", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        json = json.Replace("\"enableIntegrationWithDefaultWslDistro\":false", "\"enableIntegrationWithDefaultWslDistro\":true");
                        modified = true;
                    }

                    if (json.IndexOf("\"integratedWslDistros\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (json.IndexOf("\"" + distro + "\"", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            int arrStart = json.IndexOf("\"integratedWslDistros\"", StringComparison.OrdinalIgnoreCase);
                            int openBracket = json.IndexOf('[', arrStart);
                            if (openBracket >= 0)
                            {
                                json = json.Insert(openBracket + 1, "\n    \"" + distro + "\",");
                                modified = true;
                            }
                        }
                    }
                    else
                    {
                        int firstBrace = json.IndexOf('{');
                        if (firstBrace >= 0)
                        {
                            json = json.Insert(firstBrace + 1, "\n  \"integratedWslDistros\": [ \"" + distro + "\", \"Ubuntu\" ],");
                            modified = true;
                        }
                    }

                    if (modified)
                    {
                        File.WriteAllText(settingsFile, json, Encoding.UTF8);
                        Console.WriteLine("[+] Docker Desktop WSL2 연동 설정(settings.json) 자동 갱신 완료");
                    }
                }

                // 2. WSL 내부 docker 소켓 디렉터리 및 그룹 권한 구성 (Ubuntu 배포판이 설치된 경우에만 실행)
                if (wsl.Installed && wsl.HasUbuntu)
                {
                    string cmd = "mkdir -p /etc/docker && groupadd -f docker && (id -un 1000 2>/dev/null | xargs -I {} usermod -aG docker {})";
                    RunProcess("wsl.exe", string.Format("-d {0} -u root -- bash -c \"{1}\"", distro, cmd), true, 10000);
                    Console.WriteLine("[+] WSL 리눅스 docker 그룹 권한 구성 완료");
                }
            }
            catch { }
        }

        static void FixWSLDockerPermission()
        {
            ConfigureDockerWSLIntegration();
        }

        static void CreateRobustWSLShortcut(string distroName)
        {
            string target = @"C:\Windows\System32\cmd.exe";
            string args = string.Format(
                "/k \"wsl.exe ~ -d {0} 2>nul || wsl.exe ~ -d Ubuntu 2>nul || wsl.exe ~ 2>nul || " +
                "(echo. && echo [WSL 실행 안내] WSL 배포판을 시작할 수 없습니다. && " +
                "echo PC 재부팅 후 또는 'wsl -l -v' 로 등록 상태를 확인하세요. && echo. && pause)\"",
                distroName);

            string icon = @"C:\Windows\System32\wsl.exe,0";
            CreateShortcut("Ubuntu 24.04 LTS", target, args, icon, "Ubuntu 24.04 LTS Linux Shell");

            string whereWt = RunProcessWithOutput("where.exe", "wt").Trim();
            if (!string.IsNullOrEmpty(whereWt))
            {
                string wtArgs = string.Format("/c start wt.exe wsl.exe ~ -d {0}", distroName);
                CreateShortcut("Ubuntu (Windows Terminal)", target, wtArgs, icon, "Ubuntu in Windows Terminal");
            }
        }

        static void RepairEnvironmentAndShortcuts()
        {
            var git = DetectGit();
            if (git.Installed && !string.IsNullOrEmpty(git.Path))
            {
                AddToSystemPath(git.Path);
            }

            var py = DetectPython();
            if (py.Installed && Directory.Exists(py.Path))
            {
                AddToSystemPath(py.Path);
                string s = Path.Combine(py.Path, "Scripts");
                if (Directory.Exists(s)) AddToSystemPath(s);
                FixPythonStoreAlias(py.Path);
                CreateShortcut("Python (명령줄)", Path.Combine(py.Path, "python.exe"), "", Path.Combine(py.Path, "python.exe") + ",0", "Python");
            }

            var code = DetectVSCode();
            if (code.Installed && File.Exists(code.CodeCmd))
            {
                AddToSystemPath(Path.GetDirectoryName(code.CodeCmd));
                CreateShortcut("Visual Studio Code", Path.Combine(code.Path, "Code.exe"), "", Path.Combine(code.Path, "Code.exe") + ",0", "VS Code");
            }

            var docker = DetectDocker();
            if (docker.Installed)
            {
                if (!string.IsNullOrEmpty(docker.BinPath) && Directory.Exists(docker.BinPath))
                {
                    AddToSystemPath(docker.BinPath);
                }
                if (!string.IsNullOrEmpty(docker.Exe) && File.Exists(docker.Exe))
                {
                    CreateShortcut("Docker Desktop", docker.Exe, "", docker.Exe + ",0", "Docker");
                }
            }

            var wsl = DetectWSL();
            CreateRobustWSLShortcut(wsl.HasUbuntu ? wsl.DistroName : "Ubuntu-24.04");
            FixWSLDockerPermission();

            var discord = DetectDiscord();
            if (discord.Installed && File.Exists(discord.Exe))
            {
                CreateShortcut("Discord", discord.Exe, "", discord.Exe + ",0", "Discord");
            }

            // 바탕화면 중복 바로가기 정리 (공용 바탕화면과 개인 바탕화면에 중복 존재 시 개인 쪽 삭제하여 아이콘 1개만 유지)
            try
            {
                string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                string[] lnks = { "Docker Desktop.lnk", "Visual Studio Code.lnk", "Discord.lnk", "Ubuntu 24.04 LTS (WSL).lnk", "Python (명령줄).lnk" };
                foreach (var lnk in lnks)
                {
                    string pub = Path.Combine(publicDesktop, lnk);
                    string usr = Path.Combine(userDesktop, lnk);
                    if (File.Exists(pub) && File.Exists(usr))
                    {
                        try { File.Delete(usr); } catch { }
                    }
                }
            }
            catch { }
        }

        #endregion

        #region Multi-Tier Deep Detection Engine

        public class DiscordInfo { public bool Installed; public string Version; public string Exe; public string Path; }
        static DiscordInfo DetectDiscord()
        {
            var res = new DiscordInfo { Installed = false, Version = "", Exe = "", Path = "" };

            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string discordDir = Path.Combine(localApp, "Discord");

            if (Directory.Exists(discordDir))
            {
                string updateExe = Path.Combine(discordDir, "Update.exe");
                try
                {
                    var appDirs = Directory.GetDirectories(discordDir, "app-*");
                    Array.Sort(appDirs);
                    if (appDirs.Length > 0)
                    {
                        string latestAppDir = appDirs[appDirs.Length - 1];
                        string appExe = Path.Combine(latestAppDir, "Discord.exe");
                        if (File.Exists(appExe))
                        {
                            res.Installed = true;
                            res.Exe = appExe;
                            res.Path = latestAppDir;
                            res.Version = Path.GetFileName(latestAppDir).Replace("app-", "");
                        }
                    }
                }
                catch { }

                if (!res.Installed && File.Exists(updateExe))
                {
                    res.Installed = true;
                    res.Exe = updateExe;
                    res.Path = discordDir;
                }
            }

            if (!res.Installed)
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Discord"))
                    {
                        if (k != null)
                        {
                            string loc = k.GetValue("InstallLocation") as string;
                            string ver = k.GetValue("DisplayVersion") as string;
                            if (!string.IsNullOrEmpty(loc) && Directory.Exists(loc))
                            {
                                res.Installed = true;
                                res.Version = ver ?? "";
                                res.Path = loc;
                                res.Exe = Path.Combine(loc, "Discord.exe");
                            }
                        }
                    }
                }
                catch { }
            }

            return res;
        }

        public class GitInfo { public bool Installed; public string Version; public string Path; }
        static GitInfo DetectGit()
        {
            var res = new GitInfo { Installed = false, Version = "", Path = "" };

            // Tier 1: where.exe git
            string whereOut = RunProcessWithOutput("where.exe", "git.exe").Trim();
            if (!string.IsNullOrEmpty(whereOut))
            {
                string[] lines = whereOut.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string p = line.Trim();
                    if (File.Exists(p) && p.IndexOf("Git", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        res.Installed = true;
                        res.Path = Path.GetDirectoryName(p);
                        break;
                    }
                }
            }

            // Tier 2: Registry (HKLM & HKCU)
            if (!res.Installed)
            {
                string[] rootKeys = {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Git_is1",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Git_is1"
                };

                foreach (var keyPath in rootKeys)
                {
                    try
                    {
                        using (var k = Registry.LocalMachine.OpenSubKey(keyPath))
                        {
                            if (k != null)
                            {
                                string loc = k.GetValue("InstallLocation") as string;
                                string ver = k.GetValue("DisplayVersion") as string;
                                if (!string.IsNullOrEmpty(loc) && Directory.Exists(loc))
                                {
                                    res.Installed = true;
                                    res.Version = ver ?? "";
                                    res.Path = Path.Combine(loc, "cmd");
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            // Tier 3: Well-known paths
            if (!res.Installed)
            {
                string[] candidates = {
                    @"C:\Program Files\Git\cmd",
                    @"C:\Program Files (x86)\Git\cmd",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Git\cmd")
                };

                foreach (var c in candidates)
                {
                    if (File.Exists(Path.Combine(c, "git.exe")))
                    {
                        res.Installed = true;
                        res.Path = c;
                        break;
                    }
                }
            }

            // Tier 4: Version probe
            if (res.Installed)
            {
                string gitExe = !string.IsNullOrEmpty(res.Path) ? Path.Combine(res.Path, "git.exe") : "git.exe";
                if (!File.Exists(gitExe)) gitExe = "git.exe";
                string outVer = RunProcessWithOutput(gitExe, "--version").Trim();
                if (!string.IsNullOrEmpty(outVer))
                {
                    string[] tokens = outVer.Split(' ');
                    foreach (var tok in tokens)
                    {
                        if (tok.Contains(".") && char.IsDigit(tok[0]))
                        {
                            res.Version = tok.Trim();
                            break;
                        }
                    }
                }
            }

            return res;
        }

        public class PythonInfo { public bool Installed; public string Version; public string Path; }
        static PythonInfo DetectPython()
        {
            var res = new PythonInfo { Installed = false, Version = "", Path = "" };

            // Tier 1: py.exe
            string pyLauncherOut = RunProcessWithOutput("py.exe", "-0p").Trim();
            if (!string.IsNullOrEmpty(pyLauncherOut))
            {
                string[] lines = pyLauncherOut.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    int colonIdx = line.IndexOf(":\\", StringComparison.Ordinal);
                    if (colonIdx > 0)
                    {
                        string exePath = line.Substring(colonIdx - 1).Trim();
                        if (File.Exists(exePath))
                        {
                            string ver = RunProcessWithOutput(exePath, "-c \"import sys; print(str(sys.version_info[0])+'.'+str(sys.version_info[1])+'.'+str(sys.version_info[2]))\"").Trim();
                            if (!string.IsNullOrEmpty(ver) && ver.Contains("."))
                            {
                                res.Installed = true;
                                res.Version = ver;
                                res.Path = Path.GetDirectoryName(exePath);
                                return res;
                            }
                        }
                    }
                }
            }

            // Tier 2: Registry
            string[] pyCoreRoots = {
                @"SOFTWARE\Python\PythonCore",
                @"SOFTWARE\WOW6432Node\Python\PythonCore"
            };
            foreach (var rootKey in pyCoreRoots)
            {
                foreach (var root in new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser })
                {
                    try
                    {
                        using (var k = root.OpenSubKey(rootKey))
                        {
                            if (k != null)
                            {
                                foreach (var verName in k.GetSubKeyNames())
                                {
                                    using (var installKey = k.OpenSubKey(verName + @"\InstallPath"))
                                    {
                                        if (installKey != null)
                                        {
                                            string p = installKey.GetValue("", "") as string ?? "";
                                            if (string.IsNullOrEmpty(p)) p = installKey.GetValue("ExecutablePath", "") as string ?? "";
                                            if (!string.IsNullOrEmpty(p))
                                            {
                                                string exe = File.Exists(p) ? p : Path.Combine(p, "python.exe");
                                                if (File.Exists(exe))
                                                {
                                                    string ver = RunProcessWithOutput(exe, "-c \"import sys; print(str(sys.version_info[0])+'.'+str(sys.version_info[1])+'.'+str(sys.version_info[2]))\"").Trim();
                                                    if (!string.IsNullOrEmpty(ver) && ver.Contains("."))
                                                    {
                                                        res.Installed = true;
                                                        res.Version = ver;
                                                        res.Path = Path.GetDirectoryName(exe);
                                                        return res;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            // Tier 3: Candidates & where.exe
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            List<string> candidateDirs = new List<string> {
                @"C:\Program Files\Python312", @"C:\Program Files\Python311", @"C:\Program Files\Python310", @"C:\Program Files\Python313", @"C:\Program Files\Python39",
                Path.Combine(localApp, @"Programs\Python\Python312"),
                Path.Combine(localApp, @"Programs\Python\Python311"),
                Path.Combine(localApp, @"Programs\Python\Python310"),
                Path.Combine(localApp, @"Programs\Python\Python313"),
                @"C:\Python312", @"C:\Python311", @"C:\Python310"
            };

            string wherePy = RunProcessWithOutput("where.exe", "python").Trim();
            if (!string.IsNullOrEmpty(wherePy))
            {
                string[] lines = wherePy.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var l in lines)
                {
                    if (l.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0 && File.Exists(l))
                    {
                        candidateDirs.Insert(0, Path.GetDirectoryName(l));
                    }
                }
            }

            foreach (var d in candidateDirs)
            {
                if (string.IsNullOrEmpty(d)) continue;
                string exe = Path.Combine(d, "python.exe");
                if (File.Exists(exe))
                {
                    string ver = RunProcessWithOutput(exe, "-c \"import sys; print(str(sys.version_info[0])+'.'+str(sys.version_info[1])+'.'+str(sys.version_info[2]))\"").Trim();
                    if (!string.IsNullOrEmpty(ver) && ver.Contains("."))
                    {
                        res.Installed = true;
                        res.Version = ver;
                        res.Path = d;
                        return res;
                    }
                }
            }

            return res;
        }

        public class VSCodeInfo { public bool Installed; public string Path; public string CodeCmd; public bool KoreanExt; }
        static VSCodeInfo DetectVSCode()
        {
            var res = new VSCodeInfo { Installed = false, Path = "", CodeCmd = "", KoreanExt = false };

            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            List<string> candidateDirs = new List<string> {
                Path.Combine(progFiles, @"Microsoft VS Code"),
                Path.Combine(localApp, @"Programs\Microsoft VS Code"),
                Path.Combine(progFilesX86, @"Microsoft VS Code"),
                @"C:\Program Files\Microsoft VS Code",
                @"C:\Program Files (x86)\Microsoft VS Code"
            };

            string whereOutput = RunProcessWithOutput("where.exe", "code").Trim();
            if (!string.IsNullOrEmpty(whereOutput))
            {
                string[] lines = whereOutput.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (File.Exists(line))
                    {
                        string binDir = Path.GetDirectoryName(line);
                        string parent = Path.GetDirectoryName(binDir);
                        if (parent != null && !candidateDirs.Contains(parent)) candidateDirs.Insert(0, parent);
                    }
                }
            }

            string[] uninstallKeys = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (var uKey in uninstallKeys)
            {
                foreach (var root in new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser })
                {
                    try
                    {
                        using (var k = root.OpenSubKey(uKey))
                        {
                            if (k != null)
                            {
                                foreach (var subName in k.GetSubKeyNames())
                                {
                                    using (var sub = k.OpenSubKey(subName))
                                    {
                                        if (sub != null)
                                        {
                                            string disp = sub.GetValue("DisplayName", "") as string ?? "";
                                            if (disp.IndexOf("Visual Studio Code", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                string loc = sub.GetValue("InstallLocation", "") as string ?? "";
                                                if (!string.IsNullOrEmpty(loc) && !candidateDirs.Contains(loc))
                                                {
                                                    candidateDirs.Insert(0, loc);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            foreach (var d in candidateDirs)
            {
                if (string.IsNullOrEmpty(d)) continue;
                string exe = Path.Combine(d, "Code.exe");
                if (File.Exists(exe))
                {
                    res.Installed = true;
                    res.Path = d;
                    res.CodeCmd = Path.Combine(d, @"bin\code.cmd");
                    break;
                }
            }

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string extDir = Path.Combine(userProfile, @".vscode\extensions");
            if (Directory.Exists(extDir))
            {
                try
                {
                    foreach (var sub in Directory.GetDirectories(extDir))
                    {
                        if (Path.GetFileName(sub).IndexOf("ms-ceintl.vscode-language-pack-ko", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            res.KoreanExt = true;
                            break;
                        }
                    }
                }
                catch { }
            }

            if (!res.KoreanExt && File.Exists(res.CodeCmd))
            {
                string extList = RunProcessWithOutput(res.CodeCmd, "--list-extensions");
                if (extList.IndexOf("ms-ceintl.vscode-language-pack-ko", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    res.KoreanExt = true;
                }
            }

            return res;
        }

        public class DockerInfo { public bool Installed; public string Path; public string Exe; public string BinPath; public string Version; }
        static DockerInfo DetectDocker()
        {
            var res = new DockerInfo { Installed = false, Path = "", Exe = "", BinPath = "", Version = "" };

            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            List<string> candidateDirs = new List<string> {
                Path.Combine(localApp, @"Programs\DockerDesktop"),
                Path.Combine(progFiles, @"Docker\Docker"),
                Path.Combine(progFiles, @"Docker Desktop"),
                Path.Combine(progFilesX86, @"Docker\Docker"),
                @"C:\Program Files\Docker\Docker",
                @"C:\Program Files\Docker\Docker Desktop"
            };

            string whereOutput = RunProcessWithOutput("where.exe", "docker").Trim();
            if (!string.IsNullOrEmpty(whereOutput))
            {
                string[] lines = whereOutput.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (File.Exists(line))
                    {
                        string binDir = Path.GetDirectoryName(line);
                        string candidateDir = Path.GetDirectoryName(binDir);
                        if (candidateDir != null)
                        {
                            string parent = Path.GetDirectoryName(candidateDir);
                            if (parent != null && !candidateDirs.Contains(parent)) candidateDirs.Insert(0, parent);
                        }
                        if (!candidateDirs.Contains(binDir)) candidateDirs.Add(binDir);
                    }
                }
            }

            string[] uninstallKeys = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (var uKey in uninstallKeys)
            {
                foreach (var root in new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser })
                {
                    try
                    {
                        using (var k = root.OpenSubKey(uKey))
                        {
                            if (k != null)
                            {
                                foreach (var subName in k.GetSubKeyNames())
                                {
                                    using (var sub = k.OpenSubKey(subName))
                                    {
                                        if (sub != null)
                                        {
                                            string disp = sub.GetValue("DisplayName", "") as string ?? "";
                                            if (disp.IndexOf("Docker", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                string loc = sub.GetValue("InstallLocation", "") as string ?? "";
                                                if (!string.IsNullOrEmpty(loc) && !candidateDirs.Contains(loc))
                                                {
                                                    candidateDirs.Insert(0, loc);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            foreach (var d in candidateDirs)
            {
                if (string.IsNullOrEmpty(d)) continue;
                string exe = Path.Combine(d, "Docker Desktop.exe");
                if (File.Exists(exe))
                {
                    res.Installed = true;
                    res.Path = d;
                    res.Exe = exe;
                    string b1 = Path.Combine(d, @"resources\bin");
                    string b2 = Path.Combine(d, "bin");
                    if (Directory.Exists(b1)) res.BinPath = b1;
                    else if (Directory.Exists(b2)) res.BinPath = b2;
                    else res.BinPath = d;
                    break;
                }
            }

            string verOutput = RunProcessWithOutput("docker.exe", "--version");
            if (verOutput.IndexOf("Docker version", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                res.Installed = true;
                res.Version = verOutput.Trim();
                if (string.IsNullOrEmpty(res.Exe)) res.Exe = "docker.exe";
                if (string.IsNullOrEmpty(res.Path)) res.Path = "Docker CLI in PATH";
            }

            return res;
        }

        public class WSLInfo { public bool Installed; public bool HasUbuntu; public string DistroName; public string WslExe; }
        static WSLInfo DetectWSL()
        {
            var res = new WSLInfo { Installed = false, HasUbuntu = false, DistroName = "", WslExe = "" };

            string[] wslCandidates = {
                @"C:\Windows\System32\wsl.exe",
                @"C:\Program Files\WSL\wsl.exe"
            };
            string wslExe = "";
            foreach (var c in wslCandidates)
            {
                if (File.Exists(c)) { wslExe = c; break; }
            }
            if (string.IsNullOrEmpty(wslExe))
            {
                string whereWsl = RunProcessWithOutput("where.exe", "wsl").Trim();
                if (!string.IsNullOrEmpty(whereWsl))
                {
                    string first = whereWsl.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    if (File.Exists(first)) wslExe = first;
                }
            }

            if (!string.IsNullOrEmpty(wslExe))
            {
                res.Installed = true;
                res.WslExe = wslExe;

                string list = RunProcessWithOutput(wslExe, "-l -q").Replace("\0", "").Trim();
                string[] lines = list.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (line.IndexOf("Ubuntu", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        res.HasUbuntu = true;
                        res.DistroName = line;
                        break;
                    }
                }

                if (!res.HasUbuntu)
                {
                    string listV = RunProcessWithOutput(wslExe, "-l -v").Replace("\0", "");
                    if (listV.IndexOf("Ubuntu", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        res.HasUbuntu = true;
                        res.DistroName = "Ubuntu";
                    }
                }
            }
            return res;
        }

        public class DockerWSLInfo { public bool Configured; public string Details; }
        static DockerWSLInfo DetectDockerWSLIntegration()
        {
            var res = new DockerWSLInfo { Configured = false, Details = "미연동" };
            var wsl = DetectWSL();
            var docker = DetectDocker();

            if (!docker.Installed)
            {
                res.Details = "Docker 미설치";
                return res;
            }
            if (!wsl.Installed)
            {
                res.Details = "WSL 미설치";
                return res;
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string settingsFile = Path.Combine(appData, @"Docker\settings.json");

            bool settingsOk = false;
            string distroName = wsl.HasUbuntu ? wsl.DistroName : "Ubuntu-24.04";

            if (File.Exists(settingsFile))
            {
                try
                {
                    string json = File.ReadAllText(settingsFile);
                    bool wslEngine = json.IndexOf("\"wslEngineEnabled\": true", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     json.IndexOf("\"wslEngineEnabled\":true", StringComparison.OrdinalIgnoreCase) >= 0;

                    bool distroIntegrated = json.IndexOf(distroName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            json.IndexOf("Ubuntu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            json.IndexOf("\"enableIntegrationWithDefaultWslDistro\": true", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            json.IndexOf("\"enableIntegrationWithDefaultWslDistro\":true", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (wslEngine && distroIntegrated)
                    {
                        settingsOk = true;
                    }
                }
                catch { }
            }

            // WSL 내 런타임 소켓/CLI 확인
            bool runtimeOk = false;
            if (wsl.HasUbuntu)
            {
                string probe = RunProcessWithOutput("wsl.exe", string.Format("-d {0} -- bash -c \"test -S /var/run/docker.sock && echo 'SOCK_OK' || which docker 2>/dev/null\"", distroName)).Trim();
                if (!string.IsNullOrEmpty(probe) && (probe.Contains("SOCK_OK") || probe.Contains("docker")))
                {
                    runtimeOk = true;
                }
            }

            if (runtimeOk)
            {
                res.Configured = true;
                res.Details = distroName + " 런타임 소켓 연동됨";
            }
            else if (settingsOk)
            {
                res.Configured = true;
                res.Details = distroName + " 설정 연동됨 (Docker 실행 시 활성화)";
            }
            else
            {
                res.Configured = false;
                res.Details = File.Exists(settingsFile) ? (distroName + " 연동 옵션 미설정") : "Docker 설정 파일 생성 필요";
            }

            return res;
        }

        static bool DownloadFileWithRetry(string url, string destPath, int maxRetries = 3)
        {
            if (File.Exists(destPath))
            {
                FileInfo fi = new FileInfo(destPath);
                if (fi.Length > 1024 * 1024)
                {
                    Console.WriteLine("[*] 기존 캐시 파일 사용 (" + (fi.Length / 1024 / 1024) + " MB): " + fi.Name);
                    return true;
                }
            }

            for (int i = 1; i <= maxRetries; i++)
            {
                try
                {
                    Console.WriteLine(string.Format("[*] 고속 CDN 다운로드 시도 ({0}/{1}): {2}", i, maxRetries, url));
                    using (WebClient client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                        long lastReport = 0;
                        client.DownloadProgressChanged += (s, e) =>
                        {
                            long now = Environment.TickCount;
                            if (now - lastReport > 600 || e.ProgressPercentage == 100)
                            {
                                lastReport = now;
                                if (e.TotalBytesToReceive > 0)
                                {
                                    Console.Write(string.Format("\r[*] 다운로드 진행률: {0}% ({1:0.0}MB / {2:0.0}MB)   ",
                                        e.ProgressPercentage,
                                        e.BytesReceived / 1048576.0,
                                        e.TotalBytesToReceive / 1048576.0));
                                }
                                else
                                {
                                    Console.Write(string.Format("\r[*] 다운로드 수신 중: {0:0.0}MB   ", e.BytesReceived / 1048576.0));
                                }
                            }
                        };

                        client.DownloadFileTaskAsync(new Uri(url), destPath).GetAwaiter().GetResult();
                        Console.WriteLine();
                    }

                    if (File.Exists(destPath) && new FileInfo(destPath).Length > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("[+] 다운로드 성공: " + Path.GetFileName(destPath));
                        Console.ResetColor();
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(string.Format("[-] {0}차 다운로드 실패: {1}", i, ex.Message));
                    Console.ResetColor();
                    try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
                    System.Threading.Thread.Sleep(2000);
                }
            }
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[-] 최종 다운로드 실패: " + url);
            Console.ResetColor();
            return false;
        }

        static void AddToSystemPath(string dirPath)
        {
            if (!Directory.Exists(dirPath)) return;
            string cleanDir = Path.GetFullPath(dirPath).TrimEnd('\\');

            try
            {
                using (RegistryKey envKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", true))
                {
                    if (envKey != null)
                    {
                        string currentPath = envKey.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
                        string[] items = currentPath.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                        List<string> list = new List<string> { cleanDir };
                        foreach (var it in items)
                        {
                            string t = it.Trim().TrimEnd('\\');
                            if (!string.IsNullOrEmpty(t) && !t.Equals(cleanDir, StringComparison.OrdinalIgnoreCase) && !list.Contains(t))
                            {
                                list.Add(t);
                            }
                        }
                        string newPath = string.Join(";", list.ToArray());
                        envKey.SetValue("Path", newPath, RegistryValueKind.ExpandString);
                        Console.WriteLine("[+] 전역 PATH 등록: " + cleanDir);
                    }
                }

                string pPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (!pPath.Contains(cleanDir))
                {
                    Environment.SetEnvironmentVariable("PATH", cleanDir + ";" + pPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("PATH 등록 오류: " + ex.Message);
            }
        }

        static void FixPythonStoreAlias(string pyDir)
        {
            try
            {
                string pyExe = Path.Combine(pyDir, "python.exe");
                if (File.Exists(pyExe))
                {
                    using (RegistryKey appKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\python.exe"))
                    {
                        appKey.SetValue("", pyExe);
                        appKey.SetValue("Path", pyDir);
                    }
                    using (RegistryKey appKey3 = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\python3.exe"))
                    {
                        appKey3.SetValue("", pyExe);
                        appKey3.SetValue("Path", pyDir);
                    }
                }
            }
            catch { }
        }

        static void ConfigureVSCodeLocaleKo()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string userDir = Path.Combine(appData, @"Code\User");
                if (!Directory.Exists(userDir)) Directory.CreateDirectory(userDir);

                string argvPath = Path.Combine(userDir, "argv.json");
                string content = "{\n  \"locale\": \"ko\"\n}";
                File.WriteAllText(argvPath, content, Encoding.UTF8);
            }
            catch { }
        }

        static void CreateShortcut(string name, string target, string args, string icon, string desc)
        {
            try
            {
                string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

                string publicLnk = Path.Combine(publicDesktop, name + ".lnk");
                string userLnk = Path.Combine(userDesktop, name + ".lnk");

                // 1. 공용 바탕화면과 사용자 바탕화면에 모두 존재할 경우 사용자 바탕화면 중복 파일 정리
                if (File.Exists(publicLnk) && File.Exists(userLnk))
                {
                    try { File.Delete(userLnk); } catch { }
                }

                // 2. 이미 공용이나 사용자 바탕화면에 바로가기가 존재하는 경우 추가 생성 생략 (중복 방지)
                if (File.Exists(publicLnk) || File.Exists(userLnk))
                {
                    Console.WriteLine("[+] 바로가기 확인됨: " + name);
                    return;
                }

                // 3. 둘 다 없는 경우에만 개인 바탕화면에 1개 생성
                string psCmd = string.Format(
                    "$w=New-Object -ComObject WScript.Shell; " +
                    "$s=$w.CreateShortcut('{0}'); $s.TargetPath='{1}'; if('{2}'){{$s.Arguments='{2}'}}; if('{3}'){{$s.IconLocation='{3}'}}; if('{4}'){{$s.Description='{4}'}}; $s.Save();",
                    userLnk.Replace("'", "''"), target.Replace("'", "''"), args.Replace("'", "''"), icon.Replace("'", "''"), desc.Replace("'", "''"));

                ProcessStartInfo psi = new ProcessStartInfo("powershell", "-NoProfile -Command \"" + psCmd + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var p = Process.Start(psi);
                if (p != null) p.WaitForExit(5000);
                Console.WriteLine("[+] 바로가기 생성 완료: " + name);
            }
            catch { }
        }

        static void BroadcastEnvChange()
        {
            try
            {
                IntPtr HWND_BROADCAST = new IntPtr(0xffff);
                uint WM_SETTINGCHANGE = 0x001A;
                uint SMTO_ABORTIFHUNG = 0x0002;
                UIntPtr result;
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 5000, out result);
            }
            catch { }
        }

        static int RunProcess(string fileName, string args, bool wait, int timeoutMs = 120000)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(fileName, args)
                {
                    UseShellExecute = false
                };
                var p = Process.Start(psi);
                if (wait && p != null)
                {
                    int elapsed = 0;
                    while (!p.HasExited)
                    {
                        if (p.WaitForExit(3000)) break;
                        elapsed += 3000;
                        if (elapsed >= timeoutMs)
                        {
                            try { p.Kill(); } catch { }
                            Console.WriteLine();
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("[경고] 작업 제한 시간({0}초) 초과로 프로세스를 정리하고 다음 단계로 진행합니다: {1}", timeoutMs / 1000, fileName);
                            Console.ResetColor();
                            return -1;
                        }
                        if (elapsed % 12000 == 0)
                        {
                            Console.Write(string.Format("\r[*] 작업 진행 중... ({0}초 경과)   ", elapsed / 1000));
                        }
                    }
                    if (elapsed >= 12000) Console.WriteLine();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("프로세스 실행 예외 (" + fileName + "): " + ex.Message);
            }
            return 0;
        }

        static string RunProcessWithOutput(string fileName, string args, int timeoutMs = 15000)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(fileName, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                    }
                    return output;
                }
            }
            catch
            {
                return "";
            }
        }

        static void ShowVerifyReport()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n================================================================================");
            Console.WriteLine("                                  [설치 상태 검증]                              ");
            Console.WriteLine("================================================================================");
            Console.ResetColor();

            // 1. Git
            var git = DetectGit();
            PrintStatusRow("Git for Windows", git.Installed ? (git.Version + " (" + git.Path + ")") : "미설치", git.Installed);

            // 2. Python
            var py = DetectPython();
            PrintStatusRow("Python", py.Installed ? (py.Version + " (" + py.Path + ")") : "미설치", py.Installed);

            // 3. VS Code
            var code = DetectVSCode();
            PrintStatusRow("Visual Studio Code", code.Installed ? ("설치됨 (" + (code.KoreanExt ? "한국어팩 적용" : "한국어팩 필요") + ")") : "미설치", code.Installed);

            // 4. Docker
            var docker = DetectDocker();
            PrintStatusRow("Docker Desktop", docker.Installed ? ("설치됨 (" + docker.Path + ")") : "미설치", docker.Installed);

            // 5. WSL
            var wsl = DetectWSL();
            PrintStatusRow("WSL2 / Ubuntu", wsl.Installed ? (wsl.HasUbuntu ? (wsl.DistroName + " 활성화") : "WSL 설치됨 (배포판 필요)") : "미설치", wsl.Installed && wsl.HasUbuntu);

            // 6. Discord
            var discord = DetectDiscord();
            PrintStatusRow("Discord", discord.Installed ? (discord.Version + " (" + discord.Path + ")") : "미설치", discord.Installed);

            // 7. Docker-WSL2 통합 연동
            var dockerWsl = DetectDockerWSLIntegration();
            PrintStatusRow("Docker-WSL2 연동", dockerWsl.Configured ? dockerWsl.Details : ("연동 필요 (" + dockerWsl.Details + ")"), dockerWsl.Configured);

            // 8. 하드웨어 가상화 (VT-x / AMD SVM)
            bool biosVirt = false;
            try { biosVirt = IsProcessorFeaturePresent(21); } catch { }
            PrintStatusRow("하드웨어 가상화", biosVirt ? "활성화됨 (VT-x/SVM 정상)" : "확인 필요 (BIOS VT-x/SVM 활성화 요망)", biosVirt);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("================================================================================");
            Console.ResetColor();
        }

        static void PrintStatusRow(string name, string status, bool ok)
        {
            Console.Write(string.Format("  * {0,-22} : ", name));
            if (ok)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[PASS] " + status);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[CHECK] " + status);
            }
            Console.ResetColor();
        }

        static void PromptPostInstallReboot()
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("================================================================================");
            Console.WriteLine("                    [중요] 설치 완료 및 PC 재부팅 안내                          ");
            Console.WriteLine("================================================================================");
            Console.ResetColor();
            Console.WriteLine(" * WSL2 및 Docker Desktop의 가상화 하이퍼바이저 엔진은 Windows 재부팅 후 로드됩니다.");
            Console.WriteLine(" * 만약 재부팅 후에도 '가상화 기능' 에러가 발생한다면, PC 전원을 켤 때 BIOS(F2/Del)");
            Console.WriteLine("   설정에서 Intel Virtual Technology(VT-x) 또는 AMD SVM을 'Enabled'로 변경해주세요.");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.Write(" >> 지금 바로 PC를 다시 시작하시겠습니까? (Y/N): ");

            try
            {
                string ans = Console.ReadLine();
                if (!string.IsNullOrEmpty(ans) && ans.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\n[!] 5초 후 시스템을 다시 시작합니다... 작업 중인 창을 저장하세요.");
                    RunProcess("shutdown.exe", "/r /t 5", false);
                }
                else
                {
                    Console.WriteLine("\n[*] 나중에 수동으로 PC를 재부팅해주세요. 재부팅 완료 후 모든 기능이 정상 동작합니다.");
                }
            }
            catch { }
        }

        #endregion
    }
}
