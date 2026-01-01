using System;
using System.IO;

namespace GbxSolidTool.Core
{
    public sealed class AppPaths
    {
        public string AppBaseDir { get; }
        public string RepoRoot { get; }
        public string ToolsDir => Path.Combine(RepoRoot, "tools");

        public string PythonExe => Path.Combine(ToolsDir, "venv313", "Scripts", "python.exe");
        public string ThreeDs2Gbxml => Path.Combine(ToolsDir, "3ds2gbxml", "3ds2gbxml.py");
        public string GbxcMain => Path.Combine(ToolsDir, "gbxc", "main.py");

        public AppPaths()
        {
            AppBaseDir = AppContext.BaseDirectory;
            RepoRoot = FindRepoRoot(AppBaseDir) ?? AppBaseDir;
        }

        private static string? FindRepoRoot(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "tools")))
                    return dir.FullName;

                dir = dir.Parent;
            }
            return null;
        }

        public bool HasTools(out string message)
        {
            if (!File.Exists(PythonExe))
            {
                message = $"Missing python (venv): {PythonExe}";
                return false;
            }
            if (!File.Exists(ThreeDs2Gbxml))
            {
                message = $"Missing 3ds2gbxml.py: {ThreeDs2Gbxml}";
                return false;
            }
            if (!File.Exists(GbxcMain))
            {
                message = $"Missing gbxc main.py: {GbxcMain}";
                return false;
            }

            message = "Tools OK.";
            return true;
        }
    }
}
