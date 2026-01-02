using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GbxSolidTool.Core;

public static class DirectoryUtil
{
	public static List<string> SafeListFiles(string folder, string pattern)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
				return new List<string>();

			return Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly).ToList();
		}
		catch
		{
			return new List<string>();
		}
	}

	public static void CopyDirectory(string sourceDir, string destinationDir)
	{
		Directory.CreateDirectory(destinationDir);

		foreach (var file in Directory.GetFiles(sourceDir))
			File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);

		foreach (var dir in Directory.GetDirectories(sourceDir))
			CopyDirectory(dir, Path.Combine(destinationDir, Path.GetFileName(dir)));
	}

	public static void CopyDirectoryContents(string sourceDir, string destinationDir)
	{
		Directory.CreateDirectory(destinationDir);

		foreach (var file in Directory.GetFiles(sourceDir))
		{
			var dst = Path.Combine(destinationDir, Path.GetFileName(file));
			File.Copy(file, dst, overwrite: true);
		}

		foreach (var dir in Directory.GetDirectories(sourceDir))
		{
			var dst = Path.Combine(destinationDir, Path.GetFileName(dir));
			CopyDirectory(dir, dst);
		}
	}
}
