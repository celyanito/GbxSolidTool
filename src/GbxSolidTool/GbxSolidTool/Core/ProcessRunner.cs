using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace GbxSolidTool.Core
{
    public sealed class ProcessRunner
    {
        public async Task<int> RunAsync(
			string exePath,
	        string arguments,
	        Action<string> onStdOut,
	        Action<string> onStdErr,
	        string? workingDirectory = null,
	        CancellationToken ct = default)
		{
            var psi = new ProcessStartInfo
            {
				FileName = exePath,
				Arguments = arguments,
				WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory, //  ICI
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};

            using var process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    onStdOut?.Invoke(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    onStdErr?.Invoke(e.Data);
            };

            if (!process.Start())
                throw new InvalidOperationException("Failed to start process.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
    }
}
