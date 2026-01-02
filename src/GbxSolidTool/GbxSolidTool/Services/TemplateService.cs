using System;
using System.IO;
using System.Linq;
using GbxSolidTool.Core;

namespace GbxSolidTool.Services;

public sealed class TemplateService
{
	private readonly AppPaths _paths;
	private readonly Action<string> _log;

	public TemplateService(AppPaths paths, Action<string> log)
	{
		_paths = paths;
		_log = log;
	}

	public string PrepareTemplateForGbxc(string currentWorkDir)
	{
		var baseDir = Path.Combine(_paths.RepoRoot, "tools", "3ds2gbxml");
		if (!Directory.Exists(baseDir))
			throw new DirectoryNotFoundException($"Missing: {baseDir}");

		var solidPath = Directory.GetFiles(baseDir, "Template.Solid.xml", SearchOption.AllDirectories)
								 .FirstOrDefault();

		if (solidPath == null)
			throw new FileNotFoundException($"Template.Solid.xml not found under: {baseDir}");

		var srcTemplateRoot = Path.GetDirectoryName(solidPath)!;
		var dstTemplate = Path.Combine(currentWorkDir, "template");

		if (Directory.Exists(dstTemplate))
			Directory.Delete(dstTemplate, recursive: true);

		Directory.CreateDirectory(dstTemplate);

		DirectoryUtil.CopyDirectoryContents(srcTemplateRoot, dstTemplate);

		_log($"Template root: {srcTemplateRoot}");
		_log($"Template copied: {dstTemplate}");

		var solid = Path.Combine(dstTemplate, "Template.Solid.xml");
		_log(File.Exists(solid) ? "Template.Solid.xml OK" : "WARNING: Template.Solid.xml missing after copy");

		return dstTemplate;
	}
}
