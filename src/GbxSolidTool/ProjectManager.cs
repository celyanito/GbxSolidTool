using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Text;
namespace GbxSolidTool
{
	public class ProjectPaths
	{
		public string ProjectDir { get; init; } = "";
		public string InputDir { get; init; } = "";
		public string GeneratedDir { get; init; } = "";
		public string TemplateDir { get; init; } = "";
		public string BuildDir { get; init; } = "";
		public string LogsDir { get; init; } = "";
		public string BaseName { get; init; } = "";
		public string Input3dsPath { get; init; } = "";
	}

	public static class ProjectManager
	{
		public static string WorkRoot => @"C:\TMTools\work";
		public static string TemplateSource => @"C:\TMTools\3ds2gbxml\TemplateModel\Basic Model";

		public static ProjectPaths CreateNewProject(string source3dsPath)
		{
			Directory.CreateDirectory(WorkRoot);

			var baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(source3dsPath));
			var projectDir = GetNextProjectDir(baseName);

			var inputDir = Path.Combine(projectDir, "input");
			var genDir = Path.Combine(projectDir, "generated");
			var templateDir = Path.Combine(projectDir, "template");
			var buildDir = Path.Combine(projectDir, "build");
			var logsDir = Path.Combine(projectDir, "logs");

			Directory.CreateDirectory(projectDir);
			Directory.CreateDirectory(inputDir);
			Directory.CreateDirectory(genDir);
			Directory.CreateDirectory(templateDir);
			Directory.CreateDirectory(buildDir);
			Directory.CreateDirectory(logsDir);

			// Copy input 3ds into project/input
			var dest3ds = Path.Combine(inputDir, $"{baseName}.3ds");
			File.Copy(source3dsPath, dest3ds, overwrite: true);

			return new ProjectPaths
			{
				ProjectDir = projectDir,
				InputDir = inputDir,
				GeneratedDir = genDir,
				TemplateDir = templateDir,
				BuildDir = buildDir,
				LogsDir = logsDir,
				BaseName = baseName,
				Input3dsPath = dest3ds
			};
		}

		public static void CopyTemplateInto(ProjectPaths p)
		{
			if (!Directory.Exists(TemplateSource))
				throw new DirectoryNotFoundException($"Template source not found: {TemplateSource}");

			// Copy entire template source -> project/template
			CopyDirectory(TemplateSource, p.TemplateDir, overwrite: true);
		}

		public static void CopyGeneratedXmlIntoTemplate(ProjectPaths p)
		{
			// Copy all XML produced by 3ds2gbxml into template folder (flat)
			foreach (var xml in Directory.GetFiles(p.GeneratedDir, "*.xml", SearchOption.AllDirectories))
			{
				var name = Path.GetFileName(xml);
				var dest = Path.Combine(p.TemplateDir, name);
				File.Copy(xml, dest, overwrite: true);
			}
		}

		public static (string surfaceXml, List<string> visualXmls) DetectGeneratedXml(ProjectPaths p)
		{
			var all = Directory.GetFiles(p.GeneratedDir, "*.xml", SearchOption.AllDirectories);

			var surface = all.FirstOrDefault(f =>
				Path.GetFileName(f).Contains("CPlugSurface", StringComparison.OrdinalIgnoreCase));

			var visuals = all
				.Where(f => Path.GetFileName(f).Contains("CPlugVisual", StringComparison.OrdinalIgnoreCase))
				.Select(Path.GetFileName) // IMPORTANT: only filename; we copy into template root
				.Where(n => n != null)
				.Cast<string>()
				.OrderBy(n => n)
				.ToList();

			if (surface == null)
				throw new FileNotFoundException("No CPlugSurface*.xml found in generated folder.");

			if (visuals.Count == 0)
				throw new FileNotFoundException("No CPlugVisual*.xml found in generated folder.");

			return (Path.GetFileName(surface)!, visuals);
		}

	
		
	

		static void WriteTextUtf8NoBom(string path, string content)
	{
		// UTF-8 sans BOM (EF BB BF)
		File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
	}


	public static void PatchRootCPlugTree(ProjectPaths proj, string surfaceXml)
	{
		/*string rootPath = Path.Combine(proj.TemplateDir, "Root.CPlugTree.xml");

		//  SNAPSHOT AVANT
		File.Copy(rootPath, rootPath + ".before.xml", overwrite: true);

		// ===== PATCH =====
		string xml = File.ReadAllText(rootPath);

		//  tes modifs ici
		xml = xml.Replace("...", "...");

		// écriture
		File.WriteAllText(rootPath, xml);

		//  SNAPSHOT APRÈS
		File.Copy(rootPath, rootPath + ".after.xml", overwrite: true);*/
	}


	public static void PatchModelCPlugTree_MVP(ProjectPaths p, string visualFileName, string materialRef = "Sand")
	{
		/*var modelPath = Path.Combine(p.TemplateDir, "Model.CPlugTree.xml");
		if (!File.Exists(modelPath))
			throw new FileNotFoundException($"Model.CPlugTree.xml not found: {modelPath}");

		var doc = XDocument.Load(modelPath, LoadOptions.PreserveWhitespace);

		// Replace first visual link
		var visualLinkAttr = doc.Descendants()
			.Attributes("link")
			.FirstOrDefault(a => a.Value.Contains("CPlugVisual", StringComparison.OrdinalIgnoreCase));

		if (visualLinkAttr == null)
			throw new InvalidOperationException("Could not find a Visual link node (attribute link='...CPlugVisual...') in Model.CPlugTree.xml");

		visualLinkAttr.Value = visualFileName;

		// Replace first material ref node: attribute ref="Sand" or whatever
		var matRefAttr = doc.Descendants()
			.Attributes("ref")
			.FirstOrDefault(a =>
				// A bit heuristic, but matches the template style
				string.Equals(a.Value, "Sand", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(a.Value, "Asphalt", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(a.Value, "Grass", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(a.Value, "Dirt", StringComparison.OrdinalIgnoreCase));

		if (matRefAttr != null)
			matRefAttr.Value = materialRef;

		doc.Save(modelPath);*/
	}

	private static string GetNextProjectDir(string baseName)
	{
		for (int i = 1; i < 10000; i++)
		{
			var suffix = i.ToString("D3");
			var dir = Path.Combine(WorkRoot, $"{baseName}_no_{suffix}");
			if (!Directory.Exists(dir))
				return dir;
		}
		throw new InvalidOperationException("Could not allocate a new project directory (too many).");
	}

	private static string SanitizeFileName(string name)
	{
		var invalid = Path.GetInvalidFileNameChars();
		var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
		return string.IsNullOrWhiteSpace(cleaned) ? "Model" : cleaned;
	}

	private static void CopyDirectory(string sourceDir, string destDir, bool overwrite)
	{
		Directory.CreateDirectory(destDir);

		foreach (var file in Directory.GetFiles(sourceDir))
		{
			var dest = Path.Combine(destDir, Path.GetFileName(file));
			File.Copy(file, dest, overwrite);
		}

		foreach (var dir in Directory.GetDirectories(sourceDir))
		{
			var dest = Path.Combine(destDir, Path.GetFileName(dir));
			CopyDirectory(dir, dest, overwrite);
		}
	}
}
}
