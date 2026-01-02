using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GbxSolidTool.Core;

namespace GbxSolidTool.Services;

public sealed class WorkDirService
{
	private readonly AppPaths _paths;
	private readonly Action<string> _log;

	public WorkDirService(AppPaths paths, Action<string> log)
	{
		_paths = paths;
		_log = log;
	}

	public string PrepareWorkDirForModel(string source3dsPath, out string projectDir)
	{
		var name = Path.GetFileNameWithoutExtension(source3dsPath);
		var workRoot = Path.Combine(_paths.RepoRoot, "work");
		Directory.CreateDirectory(workRoot);

		var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		projectDir = Path.Combine(workRoot, $"{name}_{stamp}");

		var inputDir = Path.Combine(projectDir, "input");
		var xmlDir = Path.Combine(projectDir, "xml");
		var buildDir = Path.Combine(projectDir, "build");

		Directory.CreateDirectory(inputDir);
		Directory.CreateDirectory(xmlDir);
		Directory.CreateDirectory(buildDir);

		var dst3ds = Path.Combine(inputDir, Path.GetFileName(source3dsPath));
		File.Copy(source3dsPath, dst3ds, overwrite: true);

		return dst3ds;
	}

	public void CollectXmlOutputs(string workDir)
	{
		var xmlTarget = Path.Combine(workDir, "xml");
		Directory.CreateDirectory(xmlTarget);

		bool IsInsideXmlFolder(string path)
		{
			var rel = Path.GetRelativePath(workDir, path);
			var first = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
						   .FirstOrDefault();
			return string.Equals(first, "xml", StringComparison.OrdinalIgnoreCase);
		}

		var xmlFiles = Directory.GetFiles(workDir, "*.xml", SearchOption.AllDirectories)
								.Where(p => !IsInsideXmlFolder(p))
								.ToList();

		int moved = 0;

		foreach (var src in xmlFiles)
		{
			var dst = Path.Combine(xmlTarget, Path.GetFileName(src));

			try
			{
				if (File.Exists(dst))
					File.Delete(dst);

				File.Move(src, dst);
				moved++;
			}
			catch (Exception ex)
			{
				_log($"WARN: move failed for {src} -> {dst} ({ex.Message}), trying copy+delete");
				File.Copy(src, dst, overwrite: true);
				try { File.Delete(src); } catch { }
				moved++;
			}
		}

		TryDeleteEmptyFolders(workDir, xmlTarget);
		_log($"Collected XML: moved {moved} file(s) into: {xmlTarget}");
	}

	public void InjectXmlIntoTemplate(string workDir, string templateDir)
	{
		var xmlDir = Path.Combine(workDir, "xml");
		if (!Directory.Exists(xmlDir))
			throw new DirectoryNotFoundException($"XML folder not found: {xmlDir}");

		foreach (var src in Directory.GetFiles(xmlDir, "*.xml"))
		{
			var dst = Path.Combine(templateDir, Path.GetFileName(src));
			File.Copy(src, dst, overwrite: true);
		}
	}

	private void TryDeleteEmptyFolders(string root, string keepFolder)
	{
		try
		{
			foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
										 .OrderByDescending(d => d.Length))
			{
				if (string.Equals(Path.GetFullPath(dir).TrimEnd('\\'),
								  Path.GetFullPath(keepFolder).TrimEnd('\\'),
								  StringComparison.OrdinalIgnoreCase))
					continue;

				var name = new DirectoryInfo(dir).Name;
				if (name.Equals("input", StringComparison.OrdinalIgnoreCase) ||
					name.Equals("xml", StringComparison.OrdinalIgnoreCase) ||
					name.Equals("build", StringComparison.OrdinalIgnoreCase))
					continue;

				if (!Directory.EnumerateFileSystemEntries(dir).Any())
					Directory.Delete(dir, false);
			}
		}
		catch
		{
			// best-effort
		}
	}
}
