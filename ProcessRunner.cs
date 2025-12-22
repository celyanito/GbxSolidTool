using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace GbxSolidTool
{
	public static class ProcessRunner
	{
		public static async Task<int> RunAsync(
			string exePath,
			string arguments,
			string? workingDir,
			Action<string> onLine)
		{
			var psi = new ProcessStartInfo
			{
				FileName = exePath,
				Arguments = arguments,
				WorkingDirectory = workingDir ?? "",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8
			};

			using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

			p.OutputDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };
			p.ErrorDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };

			try
			{
				if (!p.Start())
				{
					onLine("ERROR: Failed to start process.");
					return -1;
				}
			}
			catch (Exception ex)
			{
				onLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
				return -1;
			}

			p.BeginOutputReadLine();
			p.BeginErrorReadLine();

			await p.WaitForExitAsync();
			return p.ExitCode;
		}
	}
}
