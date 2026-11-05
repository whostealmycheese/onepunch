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

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPhysicallyInstalledSystemMemory(out ulong TotalMemoryInKilobytes);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public enum NetJoinStatus
        {
            NetSetupUnknownStatus = 0,
            NetSetupUnjoined,
            NetSetupWorkgroupName,
            NetSetupDomainName
        }

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int NetGetJoinInformation(
            string server,
            out IntPtr domain,
            out NetJoinStatus status);

        [DllImport("netapi32.dll")]
        public static extern int NetApiBufferFree(IntPtr Buffer);

        static void SafeClear()
        {
            try { Console.Clear(); } catch { }
        }

        static void SafeReadKey()
        {
            try { Console.ReadKey(true); } catch { }
        }

        static string ReadLineWithVisibleEcho()
        {
            try { Console.CursorVisible = true; } catch { }
            Console.ForegroundColor = ConsoleColor.White;
            StringBuilder sb = new StringBuilder();
            try
            {
                while (Console.KeyAvailable) Console.ReadKey(true);

                while (true)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Enter)
                    {
                        Console.WriteLine();
                        break;
                    }
                    if (key.Key == ConsoleKey.Backspace)
                    {
                        if (sb.Length > 0)
                        {
                            sb.Remove(sb.Length - 1, 1);
                            Console.Write("\b \b");
                        }
                    }
                    else if (!char.IsControl(key.KeyChar))
                    {
                        sb.Append(key.KeyChar);
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write(key.KeyChar);
                    }
                }
            }
            catch
            {
                return Console.ReadLine() ?? "";
            }
            Console.ResetColor();
            return sb.ToString().Trim();
        }

        #region Active Directory & Genuine System Specs Detection

        public class SystemSpecInfo
        {
            public string CpuName = "";
            public int Cores = Environment.ProcessorCount;
            public ulong RamGb = 0;
            public string GpuName = "";
            public string Domain = Environment.UserDomainName;
            public string User = Environment.UserName;
            public bool IsDomainJoined = false;
        }

        static SystemSpecInfo DetectSystemSpecs()
        {
            var info = new SystemSpecInfo();
            info.User = Environment.UserName;
            info.Domain = Environment.UserDomainName;
            info.Cores = Environment.ProcessorCount;

            // 1. Genuine Active Directory Domain detection via Win32 netapi32.dll
            try
            {
                IntPtr pDomain;
                NetJoinStatus joinStatus;
                int res = NetGetJoinInformation(null, out pDomain, out joinStatus);
                if (res == 0 && pDomain != IntPtr.Zero)
                {
                    try
                    {
                        string domainName = Marshal.PtrToStringUni(pDomain);
                        if (!string.IsNullOrEmpty(domainName))
                        {
                            info.Domain = domainName;
                        }
                    }
                    finally
                    {
                        NetApiBufferFree(pDomain);
                    }
                    info.IsDomainJoined = (joinStatus == NetJoinStatus.NetSetupDomainName);
                }
                else
                {
                    info.IsDomainJoined = !string.IsNullOrEmpty(info.Domain) &&
                                          !info.Domain.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                info.IsDomainJoined = !string.IsNullOrEmpty(info.Domain) &&
                                      !info.Domain.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
            }

            // 2. Real CPU Detection via Registry
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (key != null)
                    {
                        string cpu = key.GetValue("ProcessorNameString") as string;
                        if (!string.IsNullOrEmpty(cpu))
                        {
                            info.CpuName = System.Text.RegularExpressions.Regex.Replace(cpu.Trim(), @"\s+", " ");
                        }
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(info.CpuName))
            {
                string envCpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
                info.CpuName = !string.IsNullOrEmpty(envCpu) ? envCpu.Trim() : "표준 x64 프로세서";
            }

            // 3. Real RAM Detection via Win32 API
            try
            {
                ulong memKb;
                if (GetPhysicallyInstalledSystemMemory(out memKb) && memKb > 0)
                {
                    info.RamGb = (ulong)Math.Round((double)memKb / (1024.0 * 1024.0));
                }
            }
            catch { }

            if (info.RamGb == 0)
            {
                try
                {
                    MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
                    memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                    if (GlobalMemoryStatusEx(ref memStatus) && memStatus.ullTotalPhys > 0)
                    {
                        info.RamGb = (ulong)Math.Round((double)memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0));
                    }
                }
                catch { }
            }

            // 4. Real GPU Detection via Registry (prioritize dedicated NVIDIA / AMD / Arc)
            try
            {
                using (var baseKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (baseKey != null)
                    {
                        List<string> dedicatedGpus = new List<string>();
                        List<string> otherGpus = new List<string>();

                        foreach (var sub in baseKey.GetSubKeyNames())
                        {
                            if (sub.StartsWith("0"))
                            {
                                using (var devKey = baseKey.OpenSubKey(sub))
                                {
                                    if (devKey != null)
                                    {
                                        string desc = devKey.GetValue("DriverDesc") as string;
                                        if (!string.IsNullOrEmpty(desc))
                                        {
                                            desc = desc.Trim();
                                            if (desc.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                desc.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                desc.IndexOf("Citrix", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                continue;
                                            }

                                            if (desc.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                desc.IndexOf("GeForce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                desc.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                desc.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                if (!dedicatedGpus.Contains(desc)) dedicatedGpus.Add(desc);
                                            }
                                            else
                                            {
                                                if (!otherGpus.Contains(desc)) otherGpus.Add(desc);
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        List<string> allGpus = new List<string>();
                        allGpus.AddRange(dedicatedGpus);
                        allGpus.AddRange(otherGpus);

                        if (allGpus.Count > 0)
                        {
                            info.GpuName = string.Join(" / ", allGpus.ToArray());
                        }
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(info.GpuName))
            {
                info.GpuName = "표준 디스플레이 어댑터";
            }

            return info;
        }

        static void ConfigureDockerUsersGroup()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(" [*] Active Directory(AD) 및 다중 사용자용 'docker-users' 그룹 권한을 설정하고 있습니다...");
            Console.ResetColor();
            System.Threading.Thread.Sleep(400);

            try
            {
                // 1. docker-users 로컬 그룹 생성 (이미 존재 시 발생하는 1379 메시지는 정상 처리하되 콘솔 출력 방지)
                RunProcessQuiet("net.exe", "localgroup docker-users /add", 10000);

                // 2. 현재 로그인 계정 (도메인 계정 포함: DOMAIN\\user 및 user) 추가 (이미 등록된 경우 1378 무시)
                string domainUser = Environment.UserDomainName + "\\" + Environment.UserName;
                RunProcessQuiet("net.exe", string.Format("localgroup docker-users \"{0}\" /add", domainUser), 10000);
                RunProcessQuiet("net.exe", string.Format("localgroup docker-users \"{0}\" /add", Environment.UserName), 10000);

                // 3. Authenticated Users (인증된 사용자: S-1-5-11) 추가 -> AD 도메인으로 로그인하는 모든 학생이 Docker 권한 자동 획득
                try
                {
                    SecurityIdentifier authSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
                    NTAccount authAccount = (NTAccount)authSid.Translate(typeof(NTAccount));
                    if (authAccount != null && !string.IsNullOrEmpty(authAccount.Value))
                    {
                        RunProcessQuiet("net.exe", string.Format("localgroup docker-users \"{0}\" /add", authAccount.Value), 10000);
                    }
                }
                catch { }

                // 4. GNIA 학생 도메인 그룹 추가 시도
                RunProcessQuiet("net.exe", "localgroup docker-users \"GNIA\\SG-GNIA-Students-All\" /add", 10000);
                RunProcessQuiet("net.exe", "localgroup docker-users \"GNIA\\Domain Users\" /add", 10000);

                System.Threading.Thread.Sleep(400);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [✔] docker-users 그룹 권한 구성 완료 (AD 도메인 학생 계정 및 인증된 사용자 권한 부여)");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine(" [-] docker-users 그룹 구성 안내: " + ex.Message);
                Console.ResetColor();
            }
        }

        static bool IsUserInDockerGroup()
        {
            try
            {
                string output = RunProcessWithOutput("net.exe", "localgroup docker-users");
                if (output.IndexOf("System error", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                string user = Environment.UserName;
                return output.IndexOf(user, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       output.IndexOf("Authenticated Users", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       output.IndexOf("인증된 사용자", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       output.IndexOf("SG-GNIA-Students-All", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        static void ConfigureWSLHardwareTuning()
        {
            try
            {
                var specs = DetectSystemSpecs();
                ulong wslRam = specs.RamGb >= 32 ? 24 : (specs.RamGb * 3 / 4);
                if (wslRam < 4) wslRam = 4;
                int cores = specs.Cores > 0 ? specs.Cores : Environment.ProcessorCount;
                if (cores < 2) cores = 2;

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(string.Format(" [*] 하드웨어 사양 맞춤 WSL2 가상머신 최적화 구성 중... ({0} / {1}GB RAM)", specs.CpuName, specs.RamGb));
                Console.ResetColor();

                string wslConfig =
                    "# OnePunch Optimized WSL2 Configuration\n" +
                    string.Format("# Target: {0} ({1} vCPUs), {2}GB Physical RAM\n", specs.CpuName, cores, specs.RamGb) +
                    "[wsl2]\n" +
                    string.Format("memory={0}GB\n", wslRam) +
                    string.Format("processors={0}\n", cores) +
                    "swap=8GB\n" +
                    "localhostForwarding=true\n" +
                    "nestedVirtualization=true\n" +
                    "guiApplications=true\n" +
                    "autoProxy=true\n\n" +
                    "[experimental]\n" +
                    "autoMemoryReclaim=gradual\n" +
                    "sparseVhd=true\n";

                List<string> targetDirs = new List<string>();
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile) && Directory.Exists(userProfile))
                {
                    targetDirs.Add(userProfile);
                }

                string defaultUser = @"C:\Users\Default";
                if (Directory.Exists(defaultUser))
                {
                    targetDirs.Add(defaultUser);
                }

                try
                {
                    string usersBase = Path.GetDirectoryName(userProfile);
                    if (!string.IsNullOrEmpty(usersBase) && Directory.Exists(usersBase))
                    {
                        foreach (var dir in Directory.GetDirectories(usersBase))
                        {
                            string dirName = Path.GetFileName(dir);
                            if (dirName.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
                                dirName.Equals("All Users", StringComparison.OrdinalIgnoreCase)) continue;
                            if (!targetDirs.Contains(dir)) targetDirs.Add(dir);
                        }
                    }
                }
                catch { }

                foreach (var dir in targetDirs)
                {
                    try
                    {
                        File.WriteAllText(Path.Combine(dir, ".wslconfig"), wslConfig, Encoding.UTF8);
                    }
                    catch { }
                }

                System.Threading.Thread.Sleep(400);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(string.Format(" [✔] WSL2 가상머신 최적화 완료 (메모리 {0}GB, vCPU {1}개, autoMemoryReclaim 활성화)", wslRam, cores));
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine(" [-] WSL2 최적화 설정 안내: " + ex.Message);
                Console.ResetColor();
            }
        }

        static void ConfigureMachineGit()
        {
            try
            {
                var detected = DetectGit();
                string gitExe = "git.exe";
                if (detected.Installed && !string.IsNullOrEmpty(detected.Path))
                {
                    string candidate = Path.Combine(detected.Path, "git.exe");
                    if (File.Exists(candidate)) gitExe = candidate;
                }

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(" [*] 실습실 다중 사용자 및 도메인 환경용 Git 전역 설정을 적용하고 있습니다...");
                Console.ResetColor();
                System.Threading.Thread.Sleep(300);

                RunProcessQuiet(gitExe, "config --system init.defaultBranch main", 10000);
                RunProcessQuiet(gitExe, "config --system core.autocrlf true", 10000);
                RunProcessQuiet(gitExe, "config --system core.quotepath false", 10000);
                RunProcessQuiet(gitExe, "config --system --add safe.directory \"*\"", 10000);
                RunProcessQuiet(gitExe, "config --system credential.helper manager", 10000);

                System.Threading.Thread.Sleep(300);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [✔] Git 머신 전역 설정 완료 (safe.directory '*', autocrlf, Credential Manager)");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine(" [-] Git 머신 전역 설정 안내: " + ex.Message);
                Console.ResetColor();
            }
        }

        #endregion

        #region State Management & Post-Reboot Resume

        static string GetStateFilePath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OnePunch");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "state.txt");
        }

        static void SaveInstallState(string state)
        {
            try { File.WriteAllText(GetStateFilePath(), state, Encoding.UTF8); } catch { }
        }

        static string ReadInstallState()
        {
            try
            {
                string p = GetStateFilePath();
                if (File.Exists(p)) return File.ReadAllText(p).Trim();
            }
            catch { }
            return "";
        }

        static void ClearInstallState()
        {
            try
            {
                string p = GetStateFilePath();
                if (File.Exists(p)) File.Delete(p);
            }
            catch { }
        }

        static void RegisterRunOnceResume()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", true))
                {
                    if (key != null)
                    {
                        key.SetValue("OnePunchResume", "\"" + exePath + "\" /resume");
                    }
                }
            }
            catch { }
        }

        static void UnregisterRunOnceResume()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", true))
                {
                    if (key != null)
                    {
                        key.DeleteValue("OnePunchResume", false);
                    }
                }
            }
            catch { }
        }

        #endregion

        static void Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            try { Console.InputEncoding = Encoding.UTF8; } catch { }
            try { Console.CursorVisible = true; } catch { }
            try { Console.Title = "원펀치 (OnePunch) v3.0"; } catch { }
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768; // Tls12 | Tls11

            bool skipAdmin = false;
            bool isResume = false;
            string cliChoice = null;
            foreach (var arg in args)
            {
                if (arg.Equals("/noadmin", StringComparison.OrdinalIgnoreCase) || arg.Equals("--no-admin", StringComparison.OrdinalIgnoreCase))
                {
                    skipAdmin = true;
                }
                else if (arg.Equals("/resume", StringComparison.OrdinalIgnoreCase) || arg.Equals("--resume", StringComparison.OrdinalIgnoreCase))
                {
                    isResume = true;
                }
                else if (arg.Equals("/check", StringComparison.OrdinalIgnoreCase) || arg.Equals("--check", StringComparison.OrdinalIgnoreCase))
                {
                    cliChoice = "13";
                }
                else if (arg.Equals("/all", StringComparison.OrdinalIgnoreCase) || arg.Equals("--all", StringComparison.OrdinalIgnoreCase))
                {
                    cliChoice = "1";
                }
                else
                {
                    int n;
                    if (int.TryParse(arg, out n) && n >= 0 && n <= 13)
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

            // Docker Desktop 손상 설정 파일(settingsVersion 누락) 사전 자동 정리
            CleanupCorruptDockerSettings();

            // 재부팅 후 자동 이어하기 감지 (RunOnce 또는 상태 파일)
            if (!isResume && ReadInstallState() == "post_reboot_pending")
            {
                isResume = true;
            }

            if (isResume)
            {
                RunPostRebootResumePipeline();
                return;
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
                Console.Write("\n선택 [0-11]: ");
                Console.ForegroundColor = ConsoleColor.White;

                string choice = ReadLineWithVisibleEcho();
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

            try
            {
                var specs = DetectSystemSpecs();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(" * 감지 사양: ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(specs.CpuName + " (" + specs.Cores + " 코어)");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(" | ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(specs.RamGb + "GB RAM");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(" | ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(specs.GpuName);

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(" * 계정 환경: ");
                Console.ForegroundColor = specs.IsDomainJoined ? ConsoleColor.Cyan : ConsoleColor.Gray;
                Console.Write(specs.Domain + "\\" + specs.User);
                if (specs.IsDomainJoined)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(" [Active Directory 도메인 연동 환경 - 다중 사용자 모드]");
                }
                else
                {
                    Console.WriteLine(" [로컬 사용자 환경]");
                }
                Console.ResetColor();
            }
            catch { }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" * 실습실 PC는 공용이므로 퇴실 전 반드시 GitHub에 'git push' 하세요.");
            Console.ResetColor();
            Console.WriteLine("--------------------------------------------------------------------------------");
        }

        static void PrintMenu()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(" [1] 전체 설치 (1단계 완료 후 가상화 재부팅 & 자동 이어하기) [추천]");
            Console.ResetColor();
            Console.WriteLine(" [2] Git for Windows");
            Console.WriteLine(" [3] Python 3.12");
            Console.WriteLine(" [4] Visual Studio Code (한국어 언어팩 단독 구성)");
            Console.WriteLine(" [5] Docker Desktop");
            Console.WriteLine(" [6] WSL2 & Ubuntu 24.04 LTS");
            Console.WriteLine(" [7] Discord");
            Console.WriteLine(" [8] WSL 리눅스 개발 환경 설정");
            Console.WriteLine(" [9] Chocolatey 패키지 매니저 안전 설치");
            Console.WriteLine(" [10] 환경변수 및 바로가기 복구");
            Console.WriteLine(" [11] 설치 상태 검증");
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
                    Console.WriteLine("\n>>> [9. Chocolatey 패키지 매니저 안전 설치] 시작\n");
                    InstallChocolateySafe();
                    BroadcastEnvChange();
                    break;

                case "10":
                    Console.WriteLine("\n>>> [10. 환경변수 및 바로가기 복구] 시작\n");
                    RepairEnvironmentAndShortcuts();
                    BroadcastEnvChange();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n[+] 환경변수 및 바로가기 복구 완료");
                    Console.ResetColor();
                    break;

                case "11":
                case "13":
                    Console.WriteLine("\n>>> [11. 설치 상태 검증] 시작\n");
                    ShowVerifyReport();
                    break;

                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("잘못된 번호입니다. [0-11] 사이 숫자를 입력하세요.");
                    Console.ResetColor();
                    break;
            }
        }

        static void RunFullInstallationPipeline()
        {
            PerformPreFlightChecks();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n================================================================================");
            Console.WriteLine("     [원펀치 v3.0 1단계: Windows 개발 환경 및 가상화/Docker 구축 파이프라인]    ");
            Console.WriteLine("================================================================================");
            Console.ResetColor();
            Console.WriteLine(" * 학생 및 개발자를 위해 차례대로 눈에 편안한 템포로 설치를 진행합니다.");
            System.Threading.Thread.Sleep(800);

            // 1. Git for Windows
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [1/7 단계] Git for Windows 점검 및 설치");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.ResetColor();
            ExecuteGit();
            var git = DetectGit();
            PrintStepResult("Git for Windows", git.Installed, git.Version);
            System.Threading.Thread.Sleep(700);

            // 2. Python 3.12
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [2/7 단계] Python 3.12 점검 및 설치");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.ResetColor();
            ExecutePython();
            var py = DetectPython();
            PrintStepResult("Python 3.12", py.Installed, py.Version);
            System.Threading.Thread.Sleep(700);

            // 3. Visual Studio Code & 한국어 언어팩
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [3/7 단계] Visual Studio Code 점검 및 한국어 환경 구성");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.ResetColor();
            ExecuteVSCode();
            var code = DetectVSCode();
            PrintStepResult("Visual Studio Code", code.Installed, code.KoreanExt ? "한국어팩 적용 완료" : "기본 설치됨");
            System.Threading.Thread.Sleep(700);

            // 4. Discord
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [4/7 단계] Discord 점검 및 설치");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.ResetColor();
            ExecuteDiscord();
            var discord = DetectDiscord();
            PrintStepResult("Discord", discord.Installed, discord.Version);
            System.Threading.Thread.Sleep(700);

            // 5. WSL2 & Ubuntu 24.04 LTS (가상화 플랫폼 + 배포판 등록)
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [5/7 단계] WSL2 및 Ubuntu 24.04 LTS 가상화 환경 구축");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.ResetColor();
            ExecuteWSL();
            var wsl = DetectWSL();
            PrintStepResult("WSL2 / Ubuntu", wsl.Installed, wsl.HasUbuntu ? (wsl.DistroName + " 등록됨") : "WSL2 등록 대기 (재부팅 후 활성화)");
            System.Threading.Thread.Sleep(700);

            // 6. Docker Desktop (WSL2 백엔드로 무인 설치)
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [6/7 단계] Docker Desktop 점검 및 설치 (WSL2 백엔드)");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.ResetColor();
            ExecuteDocker();
            var docker = DetectDocker();
            PrintStepResult("Docker Desktop", docker.Installed, docker.Path);
            System.Threading.Thread.Sleep(700);

            // 7. Docker-WSL2 연동 및 사용자 권한 구성
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [7/7 단계] Docker Desktop - WSL2 연동 및 사용자 권한 구성");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.ResetColor();
            ConfigureDockerWSLIntegration();
            ConfigureDockerUsersGroup();
            RepairEnvironmentAndShortcuts();
            BroadcastEnvChange();
            var dockerWsl = DetectDockerWSLIntegration();
            PrintStepResult("Docker-WSL2 연동", dockerWsl.Configured, dockerWsl.Details);
            System.Threading.Thread.Sleep(700);

            // 가상화 재부팅 상태 저장 및 재부팅 요청
            SaveInstallState("post_reboot_pending");
            RegisterRunOnceResume();
            PromptPostInstallReboot();
        }

        static void RunPostRebootResumePipeline()
        {
            SafeClear();
            PrintBanner();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n================================================================================");
            Console.WriteLine("          [원펀치 v3.0 2단계: PC 재부팅 후 자동 이어하기 (Resume)]             ");
            Console.WriteLine("================================================================================");
            Console.ResetColor();
            Console.WriteLine(" * Windows 재부팅으로 WSL2 하이퍼바이저 커널이 메모리에 활성화되었습니다.");
            Console.WriteLine(" * 남은 WSL 리눅스 개발 도구 프로비저닝 및 Docker 권한 연동을 편안한 템포로 마무리합니다.\n");
            System.Threading.Thread.Sleep(800);

            // 0. 손상된 Docker 설정이 있다면 자동 정리 및 프로세스 복구
            CleanupCorruptDockerSettings();

            // 1. WSL 리눅스 내부 개발 환경 설정
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [이어하기 1/2] WSL 리눅스 개발 환경 구성 (기본 계정, 빌드 도구, Python venv)");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.ResetColor();
            ProvisionWSLLinux("");
            var wsl = DetectWSL();
            PrintStepResult("WSL 리눅스 환경", wsl.Installed && wsl.HasUbuntu, wsl.DistroName + " 개발 환경 구성 완료");
            System.Threading.Thread.Sleep(700);

            // 2. Docker-WSL2 런타임 권한 연동
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [이어하기 2/2] Docker Desktop & WSL2 런타임 소켓 연동 마무리");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.ResetColor();
            CleanupCorruptDockerSettings();
            FixWSLDockerPermission();
            var dockerWsl = DetectDockerWSLIntegration();
            PrintStepResult("Docker-WSL2 연동", dockerWsl.Configured, dockerWsl.Details);

            // 상태 정리
            ClearInstallState();
            UnregisterRunOnceResume();
            ConfigureVSCodeLocaleKo();
            RepairEnvironmentAndShortcuts();
            BroadcastEnvChange();

            // 최종 종합 검증 리포트
            ShowVerifyReport();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[축하합니다!] 모든 교육장 PC 환경 구축 및 연동이 100% 완료되었습니다!");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[★ 개발 시작 전 체크 안내 ★]");
            Console.WriteLine(" 1. VS Code: 실행 시 한글 언어로 표시됩니다. (미적용 시 VS Code 창을 닫고 다시 실행)");
            Console.WriteLine(" 2. Docker Desktop: 시스템 트레이에 고래 아이콘이 뜬 후, WSL 터미널에서 'docker ps'를 실행해보세요.");
            Console.WriteLine("    혹시 연동이 안 뜰 경우 Docker Desktop [설정(톱니바퀴)] -> [Resources] -> [WSL Integration]에서");
            Console.WriteLine("    [Ubuntu-24.04] 토글이 켜져 있는지 확인하고 [Apply & restart]를 눌러주시면 됩니다.");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\n종료하려면 아무 키나 누르세요...");
            Console.ResetColor();
            SafeReadKey();
        }

        static void InstallChocolateySafe()
        {
            Console.WriteLine("\n[설치] Chocolatey 패키지 매니저 안전 설치");

            string chocoBin = @"C:\ProgramData\chocolatey\bin";
            if (Directory.Exists(chocoBin) && File.Exists(Path.Combine(chocoBin, "choco.exe")))
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("[-] Chocolatey가 이미 설치되어 있습니다: " + chocoBin);
                Console.ResetColor();
                AddToSystemPath(chocoBin);
                return;
            }

            Console.WriteLine("[*] Chocolatey 공식 파워셸 설치 스크립트 실행 중 (무인 모드)...");
            string psCmd = "[System.Net.ServicePointManager]::SecurityProtocol = 3072; " +
                           "iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))";
            int code = RunProcess("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + psCmd + "\"", true, 180000);
            if (Directory.Exists(chocoBin)) AddToSystemPath(chocoBin);
            if (code == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[+] Chocolatey 설치 및 시스템 PATH 등록 완료");
                Console.ResetColor();
            }
        }

        static void PrintStepResult(string name, bool success, string detail)
        {
            Console.ForegroundColor = success ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine(string.Format("==> [{0}] {1}: {2}", success ? "성공" : "확인필요", name, detail));
            Console.ResetColor();
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
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(" [*] Git for Windows 설치 여부를 확인하고 있습니다...");
            Console.ResetColor();
            System.Threading.Thread.Sleep(500);

            var detected = DetectGit();
            string gitCmdDir = @"C:\Program Files\Git\cmd";

            if (detected.Installed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [✔] Git for Windows가 이미 정상 설치되어 있습니다. (버전: " + detected.Version + ")");
                Console.ResetColor();
                if (!string.IsNullOrEmpty(detected.Path)) gitCmdDir = detected.Path;
                System.Threading.Thread.Sleep(600);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(" [*] 공식 Git for Windows 설치 파일을 다운로드하고 있습니다...");
                Console.ResetColor();
                string gitVersion = "2.47.1";
                string url = "https://github.com/git-for-windows/git/releases/download/v2.47.1.windows.1/Git-2.47.1-64-bit.exe";
                string installer = Path.Combine(InstallersDir, "Git-" + gitVersion + "-64-bit.exe");

                if (DownloadFileWithRetry(url, installer))
                {
                    System.Threading.Thread.Sleep(400);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(" [*] Git for Windows를 시스템에 무인 설치하고 있습니다... (잠시만 기다려주세요)");
                    Console.ResetColor();
                    string args = "/VERYSILENT /NORESTART /NOCANCEL /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS";
                    RunProcess(installer, args, true, 180000, "Git for Windows 설치");
                    System.Threading.Thread.Sleep(500);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(" [✔] Git for Windows 설치가 완료되었습니다.");
                    Console.ResetColor();
                }

                var fresh = DetectGit();
                if (fresh.Installed && !string.IsNullOrEmpty(fresh.Path)) gitCmdDir = fresh.Path;
            }

            if (Directory.Exists(gitCmdDir))
            {
                AddToSystemPath(gitCmdDir);
            }

            ConfigureMachineGit();
        }

        static void ExecutePython()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(" [*] Python 3.12 설치 여부를 확인하고 있습니다...");
            Console.ResetColor();
            System.Threading.Thread.Sleep(500);

            var detected = DetectPython();
            string targetDir = @"C:\Program Files\Python312";

            if (detected.Installed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [✔] Python 3.12가 이미 정상 설치되어 있습니다. (버전: " + detected.Version + ")");
                Console.ResetColor();
                targetDir = detected.Path;
                System.Threading.Thread.Sleep(600);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(" [*] 공식 Python 3.12 설치 파일을 다운로드하고 있습니다...");
                Console.ResetColor();
                string pyVersion = "3.12.8";
                string url = "https://www.python.org/ftp/python/" + pyVersion + "/python-" + pyVersion + "-amd64.exe";
                string installer = Path.Combine(InstallersDir, "python-" + pyVersion + "-amd64.exe");

                if (DownloadFileWithRetry(url, installer))
                {
                    System.Threading.Thread.Sleep(400);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(" [*] Python 3.12를 시스템에 무인 설치하고 있습니다... (잠시만 기다려주세요)");
                    Console.ResetColor();
                    string args = string.Format("/quiet InstallAllUsers=1 PrependPath=1 Include_test=0 Include_pip=1 TargetDir=\"{0}\"", targetDir);
                    RunProcess(installer, args, true, 180000, "Python 3.12 설치");
                    System.Threading.Thread.Sleep(500);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(" [✔] Python 3.12 설치가 완료되었습니다.");
                    Console.ResetColor();
                }

                var fresh = DetectPython();
                if (fresh.Installed) targetDir = fresh.Path;
            }

            if (Directory.Exists(targetDir))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(" [*] 시스템 PATH 및 윈도우 스토어 가짜 바로가기 방지 설정을 구성하고 있습니다...");
                Console.ResetColor();
                System.Threading.Thread.Sleep(400);

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

                System.Threading.Thread.Sleep(400);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [✔] Python 3.12 환경 구성 및 바로가기 등록이 완료되었습니다.");
                Console.ResetColor();
            }
        }

        static void ExecuteVSCode()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(" [*] Visual Studio Code 설치 여부를 확인하고 있습니다...");
            Console.ResetColor();
            System.Threading.Thread.Sleep(500);

            var detected = DetectVSCode();
            string targetDir = @"C:\Program Files\Microsoft VS Code";
            string codeCmd = "";

            if (detected.Installed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [✔] Visual Studio Code가 이미 설치되어 있습니다. (" + detected.Path + ")");
                Console.ResetColor();
                targetDir = detected.Path;
                codeCmd = detected.CodeCmd;
                System.Threading.Thread.Sleep(600);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(" [*] 공식 Visual Studio Code 설치 파일을 다운로드하고 있습니다...");
                Console.ResetColor();
                string url = "https://update.code.visualstudio.com/latest/win32-x64/stable";
                string installer = Path.Combine(InstallersDir, "VSCodeSetup-x64.exe");

                if (DownloadFileWithRetry(url, installer))
                {
                    System.Threading.Thread.Sleep(400);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(" [*] Visual Studio Code를 시스템에 무인 설치하고 있습니다... (잠시만 기다려주세요)");
                    Console.ResetColor();
                    string args = "/VERYSILENT /NORESTART /MERGETASKS=!runcode,addtopath,desktopicon,quicklaunchicon";
                    RunProcess(installer, args, true, 180000, "VS Code 설치");
                    System.Threading.Thread.Sleep(500);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(" [✔] Visual Studio Code 설치가 완료되었습니다.");
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

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(" [*] VS Code 공식 한국어 언어팩(Korean Language Pack) 단독 설치 및 검증 중...");
                Console.ResetColor();
                System.Threading.Thread.Sleep(400);

                string koExt = "MS-CEINTL.vscode-language-pack-ko";
                RunProcessQuiet(codeCmd, "--install-extension " + koExt + " --force", 60000);

                // 사용자 요청: 다른 플러그인은 추가하지 않고 한국어 언어팩만 유지
                string[] extsToRemove = {
                    "eamodio.gitlens",
                    "github.vscode-pull-request-github",
                    "ms-vscode-remote.remote-wsl",
                    "ms-python.python"
                };
                foreach (var ext in extsToRemove)
                {
                    RunProcessQuiet(codeCmd, "--uninstall-extension " + ext, 30000);
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [✔] VS Code 한국어 언어팩 단독 설치 완료");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(" [*] VS Code 실행 즉시 한글로 열리도록 로케일 환경(argv.json: ko)을 구성하고 있습니다...");
                Console.ResetColor();
                System.Threading.Thread.Sleep(400);

                ConfigureVSCodeLocaleKo();

                string codeExe = Path.Combine(targetDir, "Code.exe");
                if (File.Exists(codeExe))
                {
                    CreateShortcut("Visual Studio Code", codeExe, "--locale=ko", codeExe + ",0", "VS Code");
                    FixStartMenuVSCodeShortcut(codeExe);
                }

                System.Threading.Thread.Sleep(400);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [✔] VS Code 한국어 환경 구성이 완료되었습니다. (켜지자마자 한글로 시작됩니다)");
                Console.ResetColor();
            }
        }

        static void ExecuteDocker()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(" [*] Docker Desktop 설치 여부를 확인하고 있습니다...");
            Console.ResetColor();
            System.Threading.Thread.Sleep(500);

            var detected = DetectDocker();
            string targetDir = @"C:\Program Files\Docker\Docker";

            if (detected.Installed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [✔] Docker Desktop이 이미 설치되어 있습니다. (" + detected.Path + ")");
                Console.ResetColor();
                targetDir = detected.Path;
                System.Threading.Thread.Sleep(600);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(" [*] 공식 Docker Desktop 설치 파일을 다운로드하고 있습니다...");
                Console.ResetColor();
                string url = "https://desktop.docker.com/win/main/amd64/Docker%20Desktop%20Installer.exe";
                string installer = Path.Combine(InstallersDir, "DockerDesktopInstaller.exe");

                if (DownloadFileWithRetry(url, installer))
                {
                    System.Threading.Thread.Sleep(400);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(" [*] Docker Desktop 무인 설치를 진행하고 있습니다... (수 분이 소요될 수 있습니다)");
                    Console.ResetColor();
                    string args = "install --quiet --accept-license --backend=wsl-2";
                    RunProcess(installer, args, true, 300000, "Docker Desktop 설치");
                    System.Threading.Thread.Sleep(500);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(" [✔] Docker Desktop 설치가 완료되었습니다.");
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
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(" [*] Windows 가상화 및 하이퍼바이저 필수 기능을 점검하고 있습니다...");
            Console.ResetColor();
            System.Threading.Thread.Sleep(500);

            try
            {
                RunProcessQuiet("dism.exe", "/online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart", 60000);
                RunProcessQuiet("dism.exe", "/online /enable-feature /featurename:VirtualMachinePlatform /all /norestart", 60000);
                RunProcessQuiet("dism.exe", "/online /enable-feature /featurename:HypervisorPlatform /all /norestart", 60000);
                RunProcessQuiet("bcdedit.exe", "/set hypervisorlaunchtype auto", 10000);
                RunProcessQuiet("wsl.exe", "--set-default-version 2", 30000);
            }
            catch { }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(" [✔] Windows 가상화 및 하이퍼바이저 필수 기능 점검 완료");
            Console.ResetColor();
            System.Threading.Thread.Sleep(500);

            var detected = DetectWSL();
            if (detected.HasUbuntu)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [✔] Ubuntu 24.04 LTS 배포판이 이미 등록되어 있습니다. (" + detected.DistroName + ")");
                Console.ResetColor();
                System.Threading.Thread.Sleep(600);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(" [*] Ubuntu 24.04 LTS 배포판을 등록하고 있습니다... (잠시만 기다려주세요)");
                Console.ResetColor();
                int code = RunProcess("wsl.exe", "--install -d Ubuntu-24.04 --no-launch", true, 300000, "Ubuntu 24.04 배포판 등록");
                if (code == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(" [✔] Ubuntu 24.04 배포판 등록이 완료되었습니다.");
                    Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(" [안내] WSL2 Ubuntu 계정 안내:");
                    Console.WriteLine("        * 원펀치가 기본 개발자 계정(developer / 1234) 및 무인 로그인을 자동 구성합니다.");
                    Console.WriteLine("        * 혹시 리눅스 프롬프트에서 직접 비밀번호를 묻더라도 화면에 글자나 별표(*)가");
                    Console.WriteLine("          보이지 않는 것은 정상이니, 원하는 비밀번호를 치신 후 [Enter]를 누르시면 됩니다.");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(" [!] 배포판 초기 등록 완료 (가상화 기능 반영을 위해 PC 재부팅이 필요합니다)");
                    Console.ResetColor();
                }
            }

            CreateRobustWSLShortcut(detected.HasUbuntu ? detected.DistroName : "Ubuntu-24.04");
            ConfigureDockerWSLIntegration();
            ConfigureWSLHardwareTuning();
        }

        static void ExecuteDiscord()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(" [*] Discord 설치 여부를 확인하고 있습니다...");
            Console.ResetColor();
            System.Threading.Thread.Sleep(500);

            var detected = DetectDiscord();

            if (detected.Installed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [✔] Discord가 이미 정상 설치되어 있습니다. (버전: " + detected.Version + ")");
                Console.ResetColor();
                System.Threading.Thread.Sleep(600);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(" [*] 공식 Discord 설치 파일을 다운로드하고 있습니다...");
                Console.ResetColor();
                string installer = Path.Combine(InstallersDir, "DiscordSetup.exe");

                string[] mirrors = new string[]
                {
                    "https://stable.dl2.discordapp.net/distro/app/stable/win/x86/1.0.9059/DiscordSetup.exe",
                    "https://discord.com/api/download?platform=win",
                    "https://discord.com/api/downloads/distributions/app/installers/latest?arch=x86&channel=stable&platform=win"
                };

                bool downloaded = false;
                foreach (var url in mirrors)
                {
                    if (DownloadFileWithRetry(url, installer, 2))
                    {
                        downloaded = true;
                        break;
                    }
                }

                if (downloaded && File.Exists(installer))
                {
                    System.Threading.Thread.Sleep(400);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(" [*] Discord 무인 설치를 진행하고 있습니다... (잠시만 기다려주세요)");
                    Console.ResetColor();
                    RunProcess(installer, "-s", true, 180000, "Discord 설치");
                    System.Threading.Thread.Sleep(500);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(" [✔] Discord 설치가 완료되었습니다.");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(" [-] Discord 다운로드에 실패하여 설치를 건너뜁니다.");
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

            // 0. 리눅스 기본 사용자 계정 자동 구성 (비밀번호 없는 sudo + docker 권한)
            // 학생들이 WSL 설치 직후 검은 창의 비밀번호 입력(화면에 글자가 안 보임)에 혼란을 겪지 않도록
            // root 권한으로 안전하게 기본 계정(developer / 1234)을 사전 프로비저닝하고 /etc/wsl.conf 기본 사용자로 지정합니다.
            Console.WriteLine("[*] WSL 리눅스 사용자 계정 및 비밀번호 자동 구성 중 (개발자 기본 계정: developer / 1234)...");
            string userSetupCmd =
                "if ! id -u 1000 >/dev/null 2>&1; then " +
                "  useradd -m -s /bin/bash -G sudo developer 2>/dev/null || true; " +
                "  echo 'developer:1234' | chpasswd 2>/dev/null || true; " +
                "  mkdir -p /etc/sudoers.d && echo 'developer ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/developer && chmod 0440 /etc/sudoers.d/developer; " +
                "  mkdir -p /etc && (grep -q '\\[user\\]' /etc/wsl.conf 2>/dev/null || printf '\\n[boot]\\nsystemd=true\\n\\n[user]\\ndefault=developer\\n' >> /etc/wsl.conf); " +
                "else " +
                "  DEF_USER=$(id -un 1000 2>/dev/null); " +
                "  if [ -n \"$DEF_USER\" ]; then " +
                "    usermod -aG sudo \"$DEF_USER\" 2>/dev/null || true; " +
                "    mkdir -p /etc/sudoers.d && echo \"$DEF_USER ALL=(ALL) NOPASSWD:ALL\" > \"/etc/sudoers.d/$DEF_USER\" && chmod 0440 \"/etc/sudoers.d/$DEF_USER\"; " +
                "    mkdir -p /etc && (grep -q '\\[user\\]' /etc/wsl.conf 2>/dev/null || printf \"\\n[boot]\\nsystemd=true\\n\\n[user]\\ndefault=$DEF_USER\\n\" >> /etc/wsl.conf); " +
                "  fi; " +
                "fi";
            RunProcess("wsl.exe", string.Format("-d {0} -u root -- bash -c \"{1}\"", distro, userSetupCmd), true, 30000);

            Console.WriteLine("[*] 패키지 설치 중 (build-essential, git, curl, venv, ruff, gh)...");
            string aptCmd = "apt-get update && apt-get install -y build-essential curl wget git unzip python3-pip python3-venv gh jq";
            RunProcess("wsl.exe", string.Format("-d {0} -u root -- bash -c \"{1}\"", distro, aptCmd), true, 180000);

            string ruffCmd = "python3 -m pip install --break-system-packages ruff 2>/dev/null || pip install ruff 2>/dev/null || true";
            RunProcess("wsl.exe", string.Format("-d {0} -u root -- bash -c \"{1}\"", distro, ruffCmd), true, 60000);

            Console.WriteLine("[*] 작업 디렉터리 (~/projects) 생성 중...");
            string projectsCmd = "id -un 1000 2>/dev/null | xargs -I {} bash -c 'mkdir -p /home/{}/projects && chown -R {}:{} /home/{}/projects'";
            RunProcess("wsl.exe", string.Format("-d {0} -u root -- bash -c \"{1}\"", distro, projectsCmd), true, 30000);

            RunProcess("wsl.exe", string.Format("-d {0} -u root -- bash -c \"git config --system init.defaultBranch main\"", distro), true, 20000);

            // NVIDIA GPU 가속 (WSL2 Direct3D / CUDA 드라이버) 연동 점검
            Console.WriteLine("[*] WSL2 리눅스 내부 NVIDIA GPU 가속 (CUDA / Direct3D) 연동 확인 중...");
            string cudaCheck =
                "if [ -e /usr/lib/wsl/lib/libcuda.so.1 ] || [ -e /dev/dxg ]; then " +
                "  ldconfig 2>/dev/null || true; " +
                "  echo '[OK] NVIDIA GPU WSL2 가속 드라이버(CUDA) 연동 감지됨'; " +
                "fi";
            RunProcess("wsl.exe", string.Format("-d {0} -u root -- bash -c \"{1}\"", distro, cudaCheck), true, 15000);

            FixWSLDockerPermission();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[+] WSL 리눅스 내부 개발 환경 설정 완료");
            Console.ResetColor();
        }

        static void CleanupCorruptDockerSettings()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string settingsFile = Path.Combine(appData, @"Docker\settings.json");
                if (File.Exists(settingsFile))
                {
                    string json = File.ReadAllText(settingsFile);
                    // settingsVersion 키가 없는 비정상 파일은 Docker Desktop 시작 시 치명적 crash(fatal: no settingsVersion found)를 유발합니다.
                    if (json.IndexOf("\"settingsVersion\"", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        try
                        {
                            foreach (var p in Process.GetProcessesByName("Docker Desktop"))
                            {
                                try { p.Kill(); } catch { }
                            }
                        }
                        catch { }

                        try { File.Delete(settingsFile); } catch { }
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("[+] Docker Desktop 손상 설정(settingsVersion 누락 파일) 자동 정리 완료");
                        Console.ResetColor();
                    }
                }
            }
            catch { }
        }

        static void UpdateDockerJsonFile(string filePath, string distro, bool requireSettingsVersion)
        {
            try
            {
                string json = "";
                if (File.Exists(filePath))
                {
                    json = File.ReadAllText(filePath, Encoding.UTF8);
                }

                if (string.IsNullOrEmpty(json) || json.Trim() == "{}")
                {
                    if (requireSettingsVersion)
                    {
                        return;
                    }
                    json = "{\n" +
                           "  \"wslEngineEnabled\": true,\n" +
                           "  \"enableIntegrationWithDefaultWslDistro\": true,\n" +
                           "  \"integratedWslDistros\": [\n" +
                           "    \"" + distro + "\",\n" +
                           "    \"Ubuntu-24.04\",\n" +
                           "    \"Ubuntu\"\n" +
                           "  ]\n" +
                           "}";
                    File.WriteAllText(filePath, json, Encoding.UTF8);
                    Console.WriteLine("[+] Docker Desktop 최신 설정 (" + Path.GetFileName(filePath) + ": " + distro + " 연동) 생성 완료");
                    return;
                }

                if (requireSettingsVersion && json.IndexOf("\"settingsVersion\"", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return;
                }

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
                    int brace = json.IndexOf('{');
                    if (brace >= 0)
                    {
                        json = json.Insert(brace + 1, "\n  \"wslEngineEnabled\": true,");
                        modified = true;
                    }
                }

                if (json.IndexOf("\"enableIntegrationWithDefaultWslDistro\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
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
                }
                else
                {
                    int brace = json.IndexOf('{');
                    if (brace >= 0)
                    {
                        json = json.Insert(brace + 1, "\n  \"enableIntegrationWithDefaultWslDistro\": true,");
                        modified = true;
                    }
                }

                if (json.IndexOf("\"integratedWslDistros\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int arrStart = json.IndexOf("\"integratedWslDistros\"", StringComparison.OrdinalIgnoreCase);
                    int openBracket = json.IndexOf('[', arrStart);
                    if (openBracket >= 0)
                    {
                        string[] distrosToAdd = { distro, "Ubuntu-24.04", "Ubuntu" };
                        foreach (var d in distrosToAdd)
                        {
                            if (json.IndexOf("\"" + d + "\"", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                json = json.Insert(openBracket + 1, "\n    \"" + d + "\",");
                                modified = true;
                            }
                        }
                    }
                }
                else
                {
                    int brace = json.IndexOf('{');
                    if (brace >= 0)
                    {
                        json = json.Insert(brace + 1, "\n  \"integratedWslDistros\": [ \"" + distro + "\", \"Ubuntu-24.04\", \"Ubuntu\" ],");
                        modified = true;
                    }
                }

                if (modified)
                {
                    File.WriteAllText(filePath, json, Encoding.UTF8);
                    Console.WriteLine("[+] Docker Desktop WSL 연동 (" + Path.GetFileName(filePath) + ": " + distro + " 토글 활성화) 갱신 완료");
                }
            }
            catch { }
        }

        static void ConfigureDockerWSLIntegration()
        {
            try
            {
                var wsl = DetectWSL();
                string distro = wsl.HasUbuntu ? wsl.DistroName : "Ubuntu-24.04";

                List<string> dockerDirs = new List<string>();
                string currentAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(currentAppData))
                {
                    dockerDirs.Add(Path.Combine(currentAppData, "Docker"));
                }

                try
                {
                    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string usersBase = Path.GetDirectoryName(userProfile);
                    if (!string.IsNullOrEmpty(usersBase) && Directory.Exists(usersBase))
                    {
                        foreach (var uDir in Directory.GetDirectories(usersBase))
                        {
                            string dDir = Path.Combine(uDir, @"AppData\Roaming\Docker");
                            if (!dockerDirs.Contains(dDir)) dockerDirs.Add(dDir);
                        }
                    }
                }
                catch { }

                foreach (var dDir in dockerDirs)
                {
                    try
                    {
                        if (!Directory.Exists(dDir)) Directory.CreateDirectory(dDir);

                        // 1. settings-store.json (최신 Docker Desktop 4.28+ 핵심 설정)
                        string storeFile = Path.Combine(dDir, "settings-store.json");
                        UpdateDockerJsonFile(storeFile, distro, false);

                        // 2. settings.json (구형 및 클래식 Docker Desktop 호환)
                        string settingsFile = Path.Combine(dDir, "settings.json");
                        UpdateDockerJsonFile(settingsFile, distro, true);
                    }
                    catch { }
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

        static void EnsureDockerDesktopRunningAndIntegrated(string distro)
        {
            try
            {
                var docker = DetectDocker();
                if (!docker.Installed) return;

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string storeFile = Path.Combine(appData, @"Docker\settings-store.json");
                string settingsFile = Path.Combine(appData, @"Docker\settings.json");

                // 손상된 파일(settingsVersion 누락) 정리
                CleanupCorruptDockerSettings();

                // Docker Desktop이 아직 설정을 안 만들었거나 프로세스가 안 돌고 있다면 1회 실행 유도
                bool isRunning = Process.GetProcessesByName("Docker Desktop").Length > 0;
                if (!File.Exists(storeFile) && !File.Exists(settingsFile) && !string.IsNullOrEmpty(docker.Exe) && File.Exists(docker.Exe))
                {
                    Console.WriteLine("[*] Docker Desktop 설정 초기화를 위해 백그라운드 기동 중 (settings-store.json 대기)...");
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = docker.Exe,
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(docker.Exe)
                        });
                    }
                    catch { }

                    for (int i = 0; i < 15; i++)
                    {
                        System.Threading.Thread.Sleep(1000);
                        if (File.Exists(storeFile) || File.Exists(settingsFile)) break;
                    }
                }

                ConfigureDockerWSLIntegration();
            }
            catch { }
        }

        static void FixWSLDockerPermission()
        {
            var wsl = DetectWSL();
            EnsureDockerDesktopRunningAndIntegrated(wsl.HasUbuntu ? wsl.DistroName : "Ubuntu-24.04");
            ConfigureDockerUsersGroup();
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
                string codeExe = Path.Combine(code.Path, "Code.exe");
                CreateShortcut("Visual Studio Code", codeExe, "--locale=ko", codeExe + ",0", "VS Code");
                FixStartMenuVSCodeShortcut(codeExe);
                ConfigureVSCodeLocaleKo();
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

            ConfigureMachineGit();
            ConfigureDockerUsersGroup();
            ConfigureWSLHardwareTuning();
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
            string storeFile = Path.Combine(appData, @"Docker\settings-store.json");
            string settingsFile = Path.Combine(appData, @"Docker\settings.json");
            string checkFile = File.Exists(storeFile) ? storeFile : (File.Exists(settingsFile) ? settingsFile : null);

            bool settingsOk = false;
            string distroName = wsl.HasUbuntu ? wsl.DistroName : "Ubuntu-24.04";

            if (checkFile != null)
            {
                try
                {
                    string json = File.ReadAllText(checkFile);
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
            else if (docker.Installed && wsl.Installed && wsl.HasUbuntu)
            {
                res.Configured = true;
                res.Details = distroName + " WSL2 백엔드 연동 준비 완료 (Docker 첫 실행 시 자동 적용)";
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
                        client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                        client.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
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

        static void SetLocaleInArgvJson(string filePath)
        {
            try
            {
                // VS Code의 JSON 파서는 UTF-8 BOM(0xEF 0xBB 0xBF)이 있으면 "Unexpected token '﻿', ... is not valid JSON" 구문 오류를 발생시킵니다.
                // 따라서 반드시 BOM이 없는 UTF-8(new UTF8Encoding(false))로 작성해야 합니다.
                var utf8NoBom = new UTF8Encoding(false);
                string dir = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string cleanJson = "{\n\t\"locale\": \"ko\"\n}\n";

                if (File.Exists(filePath))
                {
                    try
                    {
                        string text = File.ReadAllText(filePath, Encoding.UTF8).Trim();
                        if (text.StartsWith("\uFEFF")) text = text.Substring(1).Trim();

                        if (text.IndexOf("\"locale\"", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            cleanJson = System.Text.RegularExpressions.Regex.Replace(
                                text,
                                "\"locale\"\\s*:\\s*\"[^\"]*\"",
                                "\"locale\": \"ko\"");
                        }
                        else if (text.Contains("{") && text.Contains("}"))
                        {
                            int brace = text.IndexOf('{');
                            cleanJson = text.Insert(brace + 1, "\n\t\"locale\": \"ko\",");
                        }
                    }
                    catch
                    {
                        cleanJson = "{\n\t\"locale\": \"ko\"\n}\n";
                    }
                }

                File.WriteAllText(filePath, cleanJson, utf8NoBom);
            }
            catch { }
        }

        static void ConfigureVSCodeLocaleKo()
        {
            try
            {
                var utf8NoBom = new UTF8Encoding(false);
                List<string> userDirs = new List<string>();
                string currentUser = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(currentUser) && Directory.Exists(currentUser))
                {
                    userDirs.Add(currentUser);
                }

                // C:\Users\Default 프로필 등록 (새로운 Active Directory 사용자 첫 로그인 시 자동 복제)
                string defaultUser = @"C:\Users\Default";
                if (Directory.Exists(defaultUser) && !userDirs.Contains(defaultUser))
                {
                    userDirs.Add(defaultUser);
                }

                try
                {
                    string usersBase = Path.GetDirectoryName(currentUser);
                    if (!string.IsNullOrEmpty(usersBase) && Directory.Exists(usersBase))
                    {
                        foreach (var dir in Directory.GetDirectories(usersBase))
                        {
                            string dirName = Path.GetFileName(dir);
                            if (dirName.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
                                dirName.Equals("All Users", StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (!userDirs.Contains(dir)) userDirs.Add(dir);
                        }
                    }
                }
                catch { }

                foreach (var uDir in userDirs)
                {
                    try
                    {
                        // 1. <user>\.vscode\argv.json (VS Code 공식 런타임 인자 파일 - UTF-8 No-BOM 필수)
                        string dotVscode = Path.Combine(uDir, ".vscode");
                        if (!Directory.Exists(dotVscode)) Directory.CreateDirectory(dotVscode);
                        SetLocaleInArgvJson(Path.Combine(dotVscode, "argv.json"));

                        // 2. 비표준 AppData 경로의 argv.json 정리 (충돌 및 구문 오류 방지)
                        string appDataCode = Path.Combine(uDir, @"AppData\Roaming\Code");
                        string rogue1 = Path.Combine(appDataCode, "argv.json");
                        if (File.Exists(rogue1)) { try { File.Delete(rogue1); } catch { } }

                        string appDataCodeUser = Path.Combine(appDataCode, "User");
                        string rogue2 = Path.Combine(appDataCodeUser, "argv.json");
                        if (File.Exists(rogue2)) { try { File.Delete(rogue2); } catch { } }

                        // 3. <user>\AppData\Roaming\Code\User\locale.json (구버전 호환, UTF-8 No-BOM)
                        if (!Directory.Exists(appDataCodeUser)) Directory.CreateDirectory(appDataCodeUser);
                        File.WriteAllText(Path.Combine(appDataCodeUser, "locale.json"), "{\n  \"locale\": \"ko\"\n}\n", utf8NoBom);
                    }
                    catch { }
                }

                Console.WriteLine("[+] VS Code 한국어 언어팩 로케일 영구 적용 완료 (UTF-8 No-BOM .vscode/argv.json -> ko)");
            }
            catch { }
        }

        static void FixStartMenuVSCodeShortcut(string codeExe)
        {
            try
            {
                string[] startMenuDirs = {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), @"Programs\Visual Studio Code"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), @"Programs\Visual Studio Code"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs\Visual Studio Code"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs\Visual Studio Code")
                };

                foreach (var dir in startMenuDirs)
                {
                    if (Directory.Exists(dir))
                    {
                        foreach (var lnk in Directory.GetFiles(dir, "*.lnk"))
                        {
                            try
                            {
                                string psCmd = string.Format(
                                    "$w=New-Object -ComObject WScript.Shell; " +
                                    "$s=$w.CreateShortcut('{0}'); $s.Arguments='--locale=ko'; $s.Save();",
                                    lnk.Replace("'", "''"));

                                ProcessStartInfo psi = new ProcessStartInfo("powershell", "-NoProfile -Command \"" + psCmd + "\"")
                                {
                                    CreateNoWindow = true,
                                    UseShellExecute = false
                                };
                                var p = Process.Start(psi);
                                if (p != null) p.WaitForExit(3000);
                            }
                            catch { }
                        }
                    }
                }
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

                // 다중 사용자 / AD 실습실 환경에서는 모든 사용자가 접근 가능한 공용 바탕화면 우선 생성
                string targetLnk = Directory.Exists(publicDesktop) ? publicLnk : userLnk;

                string psCmd = string.Format(
                    "$w=New-Object -ComObject WScript.Shell; " +
                    "$s=$w.CreateShortcut('{0}'); $s.TargetPath='{1}'; if('{2}'){{$s.Arguments='{2}'}}; if('{3}'){{$s.IconLocation='{3}'}}; if('{4}'){{$s.Description='{4}'}}; $s.Save();",
                    targetLnk.Replace("'", "''"), target.Replace("'", "''"), args.Replace("'", "''"), icon.Replace("'", "''"), desc.Replace("'", "''"));

                ProcessStartInfo psi = new ProcessStartInfo("powershell", "-NoProfile -Command \"" + psCmd + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var p = Process.Start(psi);
                if (p != null) p.WaitForExit(5000);
                Console.WriteLine("[+] 바로가기 생성/갱신 완료: " + name + (string.IsNullOrEmpty(args) ? "" : " (인수: " + args + ")"));
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

        static int RunProcess(string fileName, string args, bool wait, int timeoutMs = 120000, string friendlyTaskName = "")
        {
            try
            {
                if (fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                {
                    args = "/c \"" + fileName + "\" " + args;
                    fileName = "cmd.exe";
                }

                ProcessStartInfo psi = new ProcessStartInfo(fileName, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
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
                        if (elapsed % 6000 == 0)
                        {
                            string taskDesc = !string.IsNullOrEmpty(friendlyTaskName) ? friendlyTaskName : Path.GetFileName(fileName);
                            Console.Write(string.Format("\r [*] {0} 작업이 백그라운드에서 진행 중입니다... ({1}초 경과)   ", taskDesc, elapsed / 1000));
                        }
                    }
                    if (elapsed >= 6000) Console.WriteLine();
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
                if (fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                {
                    args = "/c \"" + fileName + "\" " + args;
                    fileName = "cmd.exe";
                }

                ProcessStartInfo psi = new ProcessStartInfo(fileName, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    try { p.StandardError.ReadToEnd(); } catch { }
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

        static int RunProcessQuiet(string fileName, string args, int timeoutMs = 15000)
        {
            RunProcessWithOutput(fileName, args, timeoutMs);
            return 0;
        }

        static void ShowVerifyReport()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n================================================================================");
            Console.WriteLine("                                  [설치 상태 검증]                              ");
            Console.WriteLine("================================================================================");
            Console.ResetColor();

            // 0. 하드웨어 사양 및 Active Directory 도메인 환경
            var specs = DetectSystemSpecs();
            PrintStatusRow("하드웨어 CPU / RAM", specs.CpuName + " (" + specs.Cores + " 코어 / " + specs.RamGb + "GB RAM)", true);
            PrintStatusRow("GPU 그래픽 카드", specs.GpuName + " (WSL2 CUDA 지원)", true);
            PrintStatusRow("Active Directory 계정", specs.Domain + "\\" + specs.User + (specs.IsDomainJoined ? " [AD 도메인 연동]" : " [로컬]"), true);

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
            bool inDockerGroup = IsUserInDockerGroup();
            PrintStatusRow("Docker Desktop", docker.Installed ? ("설치됨 (" + docker.Path + ")") : "미설치", docker.Installed);
            PrintStatusRow("Docker 도메인 권한", inDockerGroup ? "docker-users 그룹 구성 완료 (AD 도메인 계정 허용)" : "docker-users 그룹 점검 필요", inDockerGroup);

            // 5. WSL
            var wsl = DetectWSL();
            PrintStatusRow("WSL2 / Ubuntu", wsl.Installed ? (wsl.HasUbuntu ? (wsl.DistroName + " 활성화") : "WSL 설치됨 (배포판 필요)") : "미설치", wsl.Installed && wsl.HasUbuntu);

            // 6. WSL2 가상머신 최적화 (.wslconfig)
            bool wslConfigExists = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig"));
            ulong wslRam = specs.RamGb >= 32 ? 24 : (specs.RamGb * 3 / 4);
            if (wslRam < 4) wslRam = 4;
            int wslCores = specs.Cores > 0 ? specs.Cores : Environment.ProcessorCount;
            PrintStatusRow("WSL2 하드웨어 튜닝", wslConfigExists ? string.Format("최적화 완료 ({0}GB RAM / {1} vCPU / AutoReclaim)", wslRam, wslCores) : "기본값 (.wslconfig 미생성)", wslConfigExists);

            // 7. Discord
            var discord = DetectDiscord();
            PrintStatusRow("Discord", discord.Installed ? (discord.Version + " (" + discord.Path + ")") : "미설치", discord.Installed);

            // 8. Docker-WSL2 통합 연동
            var dockerWsl = DetectDockerWSLIntegration();
            PrintStatusRow("Docker-WSL2 연동", dockerWsl.Configured ? dockerWsl.Details : ("연동 필요 (" + dockerWsl.Details + ")"), dockerWsl.Configured);

            // 9. 하드웨어 가상화 (VT-x / AMD SVM)
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
            Console.WriteLine(" * Windows 기본 개발 도구(Git, Python, VS Code, Discord) 설치가 완료되었습니다.");
            Console.WriteLine(" * WSL2 및 Docker Desktop 가상화 엔진은 Windows 재부팅 후 커널이 활성화됩니다.");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(" ------------------------------------------------------------------------------");
            Console.WriteLine(" [★필독★] PC 재부팅 후 OnePunch 실행 창(UAC 관리자 확인 창)이 뜨면");
            Console.WriteLine("           반드시 [예] 또는 [실행]을 꼭 눌러주셔야 2단계 자동 마무리가 진행됩니다!");
            Console.WriteLine(" ------------------------------------------------------------------------------");
            Console.ResetColor();
            Console.WriteLine(" * 재부팅 후에도 가상화 오류 시 부팅 중 BIOS(F2/Del)에서 VT-x/SVM을 활성화하세요.");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(" >> 지금 바로 PC를 다시 시작하시겠습니까? [Y/N]: ");
            Console.ForegroundColor = ConsoleColor.White;

            try
            {
                // 버퍼에 남아있는 이전 키 입력 비우기
                while (Console.KeyAvailable) Console.ReadKey(true);

                while (true)
                {
                    ConsoleKeyInfo key = Console.ReadKey(false);
                    char c = char.ToUpperInvariant(key.KeyChar);

                    if (key.Key == ConsoleKey.Y || c == 'Y')
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("\n\n[!] 5초 후 시스템을 다시 시작합니다... 작업 중인 문서를 저장하세요.");
                        Console.ResetColor();
                        RunProcess("shutdown.exe", "/r /t 5", false);
                        System.Threading.Thread.Sleep(1000);
                        Environment.Exit(0);
                    }
                    else if (key.Key == ConsoleKey.N || c == 'N' || key.Key == ConsoleKey.Enter || key.Key == ConsoleKey.Escape)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine("\n\n[*] 재부팅을 보류합니다.");
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine(" [★안내★] 나중에 PC를 재부팅하시면 OnePunch 실행 창이 뜹니다. 꼭 [예/실행]을 눌러주세요!");
                        Console.ResetColor();
                        break;
                    }
                }
            }
            catch
            {
                Console.ResetColor();
            }
        }

        #endregion
    }
}
