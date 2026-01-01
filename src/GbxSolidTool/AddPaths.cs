using System;
using System.IO;

namespace GbxSolidTool
{
    public class AppPaths
    {
        // Valeurs par défaut "standard" sur ta machine
        public string PythonExe { get; set; } = @"C:\TMTools\venv-3ds2gbxml\Scripts\python.exe";
        public string Repo3ds2Gbxml { get; set; } = @"C:\TMTools\3ds2gbxml";
        public string XmlRoot { get; set; } = @"C:\TMTools\xml";
        public string BuildRoot { get; set; } = @"C:\TMTools\build";

        public string EnsureModelFolder(string modelName)
        {
            var dir = Path.Combine(XmlRoot, modelName);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string SafeModelNameFromFile(string filePath)
            => Path.GetFileNameWithoutExtension(filePath).Trim();
    }
}
