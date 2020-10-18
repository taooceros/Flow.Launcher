using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using Shell;
using Flow.Launcher.Infrastructure;
using Flow.Launcher.Plugin.Program.Logger;
using Flow.Launcher.Plugin.SharedCommands;
using Windows.UI.Core;
using NLog.Filters;
using System.Text.RegularExpressions;
using ToolGood.Words.Pinyin;
using System.Reflection.PortableExecutable;

namespace Flow.Launcher.Plugin.Program.Programs
{
    [Serializable]
    public class Win32 : IProgram
    {
        public string Name { get; set; }
        public string UniqueIdentifier { get; set; }
        public string IcoPath { get; set; }
        public string FullPath { get; set; }
        public string ParentDirectory { get; set; }
        public string ExecutableName { get; set; }
        public string Description { get; set; }
        public bool Valid { get; set; }
        public bool Enabled { get; set; }
        public string Location => ParentDirectory;

        private const string ShortcutExtension = "lnk";
        private const string ApplicationReferenceExtension = "appref-ms";
        private const string ExeExtension = "exe";




        public Result Result(string query, IPublicAPI api)
        {
            string title = Description switch
            {
                string d when d.Length >= Name.Length && d[0..Name.Length] == Name => d,
                string d when !string.IsNullOrEmpty(d) => $"{Name}: {Description}",
                _ => Name
            };

            (string[] spaceSplitName, string[] upperSplitName) = Name switch
            {
                string n when WordsHelper.HasChinese(n) => (null, null),
                string n when n.Contains(' ') => (Name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), default(string[])),

                string n when n.Any(x => char.IsUpper(x)) && n.Any(x => char.IsLower(x)) => (null,
                    Regex.Split(Name, @"(?<!^)(?=[A-Z])")),
                _ => (null, null)
            };



            var acronymMatch = (spaceSplitName, upperSplitName) switch
            {
                (null, null) => null,

                (var s, null) => StringMatcher.FuzzySearch(query, string.Concat(
                    s.Select(x => x.FirstOrDefault()))),

                (null, var s) => StringMatcher.FuzzySearch(query, string.Concat(
                s.Select(x => x.FirstOrDefault()))),

                _ => null
            };

            MatchResult match = null;

            if (acronymMatch?.Score > 0)
                acronymMatch.MatchData = (spaceSplitName, upperSplitName) switch
                {
                    (var s, null) => acronymMatch.MatchData.Select((x, i) => s.Take(x).Sum(x => x.Length + 1)).ToList(),
                    (null, var u) => acronymMatch.MatchData.Select((x, i) => u.Take(x).Sum(x => x.Length)).ToList(),
                    _ => null
                };
            else
            {
                match = StringMatcher.FuzzySearch(query, title);
                acronymMatch = null;
            }

            int score;
            List<int> titleHighlightData;

            // Give the score
            score = acronymMatch?.Score ?? match?.Score ?? 0;
            titleHighlightData = acronymMatch?.MatchData ?? match?.MatchData ?? null;


            if (score == 0)
                return null;

            var result = new Result
            {
                Title = title,
                Score = score,
                TitleHighlightData = titleHighlightData,
                SubTitle = FullPath,
                IcoPath = IcoPath,
                ContextData = this,
                Action = _ =>
                {
                    var info = new ProcessStartInfo
                    {
                        FileName = FullPath,
                        WorkingDirectory = ParentDirectory,
                        UseShellExecute = true
                    };

                    Main.StartProcess(Process.Start, info);

                    return true;
                }
            };


            return result;
        }


        public List<Result> ContextMenus(IPublicAPI api)
        {
            var contextMenus = new List<Result>
            {
                new Result
                {
                    Title = api.GetTranslation("flowlauncher_plugin_program_run_as_different_user"),
                    Action = _ =>
                    {
                        var info = new ProcessStartInfo
                        {
                            FileName = FullPath,
                            WorkingDirectory = ParentDirectory,
                            UseShellExecute = true
                        };

                        Task.Run(() => Main.StartProcess(ShellCommand.RunAsDifferentUser, info));

                        return true;
                    },
                    IcoPath = "Images/user.png"
                },
                new Result
                {
                    Title = api.GetTranslation("flowlauncher_plugin_program_run_as_administrator"),
                    Action = _ =>
                    {
                        var info = new ProcessStartInfo
                        {
                            FileName = FullPath,
                            WorkingDirectory = ParentDirectory,
                            Verb = "runas",
                            UseShellExecute = true
                        };

                        Task.Run(() => Main.StartProcess(Process.Start, info));

                        return true;
                    },
                    IcoPath = "Images/cmd.png"
                },
                new Result
                {
                    Title = api.GetTranslation("flowlauncher_plugin_program_open_containing_folder"),
                    Action = _ =>
                    {
                        Main.StartProcess(Process.Start, new ProcessStartInfo("explorer", ParentDirectory));

                        return true;
                    },
                    IcoPath = "Images/folder.png"
                }
            };
            return contextMenus;
        }



        public override string ToString()
        {
            return ExecutableName;
        }

        private static Win32 Win32Program(string path)
        {
            try
            {
                var p = new Win32
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    IcoPath = path,
                    FullPath = path,
                    UniqueIdentifier = path,
                    ParentDirectory = Directory.GetParent(path).FullName,
                    Description = string.Empty,
                    Valid = true,
                    Enabled = true
                };
                return p;
            }
            catch (Exception e) when (e is SecurityException || e is UnauthorizedAccessException)
            {
                ProgramLogger.LogException($"|Win32|Win32Program|{path}" +
                                            $"|Permission denied when trying to load the program from {path}", e);

                return new Win32() { Valid = false, Enabled = false };
            }
        }

        private static Win32 LnkProgram(string path)
        {
            var program = Win32Program(path);
            try
            {
                var link = new ShellLink();
                const uint STGM_READ = 0;
                ((IPersistFile)link).Load(path, STGM_READ);
                var hwnd = new _RemotableHandle();
                link.Resolve(ref hwnd, 0);

                const int MAX_PATH = 260;
                StringBuilder buffer = new StringBuilder(MAX_PATH);

                var data = new _WIN32_FIND_DATAW();
                const uint SLGP_SHORTPATH = 1;
                link.GetPath(buffer, buffer.Capacity, ref data, SLGP_SHORTPATH);
                var target = buffer.ToString();
                if (!string.IsNullOrEmpty(target))
                {
                    var extension = Extension(target);
                    if (extension == ExeExtension && File.Exists(target))
                    {
                        buffer = new StringBuilder(MAX_PATH);
                        link.GetDescription(buffer, MAX_PATH);
                        var description = buffer.ToString();
                        if (!string.IsNullOrEmpty(description))
                        {
                            program.Description = description;
                        }
                        else
                        {
                            var info = FileVersionInfo.GetVersionInfo(target);
                            if (!string.IsNullOrEmpty(info.FileDescription))
                            {
                                program.Description = info.FileDescription;
                            }
                        }
                    }
                }
                return program;
            }
            catch (COMException e)
            {
                // C:\\ProgramData\\Microsoft\\Windows\\Start Menu\\Programs\\MiracastView.lnk always cause exception
                ProgramLogger.LogException($"|Win32|LnkProgram|{path}" +
                                                "|Error caused likely due to trying to get the description of the program", e);

                program.Valid = false;
                return program;
            }
#if !DEBUG //Only do a catch all in production. This is so make developer aware of any unhandled exception and add the exception handling in.
            catch (Exception e)
            {
                ProgramLogger.LogException($"|Win32|LnkProgram|{path}" +
                                                "|An unexpected error occurred in the calling method LnkProgram", e);

                program.Valid = false;
                return program;
            }
#endif
        }

        private static Win32 ExeProgram(string path)
        {
            try
            {
                var program = Win32Program(path);
                var info = FileVersionInfo.GetVersionInfo(path);
                program.Description = info.FileDescription;
                return program;
            }
            catch (Exception e) when (e is SecurityException || e is UnauthorizedAccessException)
            {
                ProgramLogger.LogException($"|Win32|ExeProgram|{path}" +
                                            $"|Permission denied when trying to load the program from {path}", e);

                return new Win32() { Valid = false, Enabled = false };
            }
        }

        private static IEnumerable<string> ProgramPaths(string directory, string[] suffixes)
        {
            if (!Directory.Exists(directory))
                return new string[] { };
            try
            {
                var paths = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                                     .Where(x => suffixes.Contains(Extension(x)));
                return paths;

            }
            catch (DirectoryNotFoundException e)
            {
                ProgramLogger.LogException($"Directory not found {directory}", e);
                return new string[] { };
            }
            catch (Exception e) when (e is SecurityException || e is UnauthorizedAccessException)
            {
                ProgramLogger.LogException($"Permission denied {directory}", e);
                return new string[] { };
            }
        }

        private static string Extension(string path)
        {
            var extension = Path.GetExtension(path)?.ToLower();
            if (!string.IsNullOrEmpty(extension))
            {
                return extension[1..];
            }
            else
            {
                return string.Empty;
            }
        }

        private static ParallelQuery<Win32> UnregisteredPrograms(List<Settings.ProgramSource> sources, string[] suffixes)
        {
            var paths = sources.Where(s => s.Enabled && Directory.Exists(s.Location))
                .SelectMany(s => ProgramPaths(s.Location, suffixes))
                .Where(t1 => !Main._settings.DisabledProgramSources.Any(x => t1 == x.UniqueIdentifier))
                .Distinct()
                .ToList();

            var programs = paths.AsParallel().Select(x => Extension(x) switch
            {
                ExeExtension => ExeProgram(x),
                ShortcutExtension => LnkProgram(x),
                _ => Win32Program(x)
            });


            return programs;
        }

        private static ParallelQuery<Win32> StartMenuPrograms(string[] suffixes)
        {
            var disabledProgramsList = Main._settings.DisabledProgramSources;

            var directory1 = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var directory2 = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            var paths1 = ProgramPaths(directory1, suffixes);
            var paths2 = ProgramPaths(directory2, suffixes);

            var toFilter = paths1.Concat(paths2);

            var programs = toFilter
                        .AsParallel()
                        .Where(t1 => !disabledProgramsList.Any(x => x.UniqueIdentifier == t1))
                        .Distinct()
                        .Select(x => Extension(x) switch
            {
                ShortcutExtension => LnkProgram(x),
                _ => Win32Program(x)
            }).Where(x => x.Valid);
            return programs;
        }

        private static ParallelQuery<Win32> AppPathsPrograms(string[] suffixes)
        {
            // https://msdn.microsoft.com/en-us/library/windows/desktop/ee872121
            const string appPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

            using var localRoot = Registry.LocalMachine.OpenSubKey(appPaths);
            using var userRoot = Registry.CurrentUser.OpenSubKey(appPaths);

            var programs = (localRoot, userRoot) switch
            {
                (null, null) => new List<Win32>(),
                (var l, null) => GetProgramsFromRegistry(l),
                (null, var u) => GetProgramsFromRegistry(u),
                (var l, var u) => GetProgramsFromRegistry(l).Concat(GetProgramsFromRegistry(u))
            };


            var disabledProgramsList = Main._settings.DisabledProgramSources;
            var toFilter = programs.AsParallel().Where(p => suffixes.Contains(Extension(p.ExecutableName)));

            var filtered = toFilter.Where(t1 => !disabledProgramsList.Any(x => x.UniqueIdentifier == t1.UniqueIdentifier)).Select(t1 => t1);

            return filtered;
        }

        private static IEnumerable<Win32> GetProgramsFromRegistry(RegistryKey root)
        {
            return root
                    .GetSubKeyNames()
                    .Select(x => GetProgramPathFromRegistrySubKeys(root, x))
                    .Distinct()
                    .Select(x => GetProgramFromPath(x));
        }

        private static string GetProgramPathFromRegistrySubKeys(RegistryKey root, string subkey)
        {
            var path = string.Empty;
            try
            {
                using (var key = root.OpenSubKey(subkey))
                {
                    if (key == null)
                        return string.Empty;

                    var defaultValue = string.Empty;
                    path = key.GetValue(defaultValue) as string;
                }

                if (string.IsNullOrEmpty(path))
                    return string.Empty;

                // fix path like this: ""\"C:\\folder\\executable.exe\""
                return path = path.Trim('"', ' ');
            }
            catch (Exception e) when (e is SecurityException || e is UnauthorizedAccessException)
            {
                ProgramLogger.LogException($"|Win32|GetProgramPathFromRegistrySubKeys|{path}" +
                                            $"|Permission denied when trying to load the program from {path}", e);

                return string.Empty;
            }
        }

        private static Win32 GetProgramFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return new Win32();

            path = Environment.ExpandEnvironmentVariables(path);

            if (!File.Exists(path))
                return new Win32();

            var entry = Win32Program(path);
            entry.ExecutableName = Path.GetFileName(path);

            return entry;
        }

        public static Win32[] All(Settings settings)
        {
            try
            {
                var programs = new List<Win32>().AsParallel();

                var unregistered = UnregisteredPrograms(settings.ProgramSources, settings.ProgramSuffixes);
                programs = programs.Concat(unregistered);
                if (settings.EnableRegistrySource)
                {
                    var appPaths = AppPathsPrograms(settings.ProgramSuffixes);
                    programs = programs.Concat(appPaths);
                }

                if (settings.EnableStartMenuSource)
                {
                    var startMenu = StartMenuPrograms(settings.ProgramSuffixes);
                    programs = programs.Concat(startMenu);
                }

                return programs.ToArray();
            }
#if DEBUG //This is to make developer aware of any unhandled exception and add in handling.
            catch (Exception e)
            {
                throw e;
            }
#endif

#if !DEBUG //Only do a catch all in production.
            catch (Exception e)
            {
                ProgramLogger.LogException("|Win32|All|Not available|An unexpected error occurred", e);

                return new Win32[0];
            }
#endif
        }
    }
}
