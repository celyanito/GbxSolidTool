using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GbxSolidTool.Models;

namespace GbxSolidTool.Services;

public static class TemplateTreeBuilder
{
	public sealed class BuildResult
	{
		public string RootPath { get; init; } = "";
		public string ModelElementsPath { get; init; } = "";
		public List<string> CreatedParts { get; init; } = new();
		public string? SurfaceXml { get; init; }
		public List<string> VisualXmls { get; init; } = new();
	}

	public static BuildResult BuildTrees(
		string templateDir,
		string modelName,
		List<FaceMatGroup> faceGroups,
		Action<string> log,
		Action<string> warn)
	{
		if (string.IsNullOrWhiteSpace(templateDir) || !Directory.Exists(templateDir))
			throw new DirectoryNotFoundException($"Template dir not found: {templateDir}");

		string rootPath = Path.Combine(templateDir, "Root.CPlugTree.xml");
		string modelElementsPath = Path.Combine(templateDir, "ModelElements.CPlugTree.xml");
		string leafTemplatePath = Path.Combine(templateDir, "Model.CPlugTree.xml");
		string solidPath = Path.Combine(templateDir, "Template.Solid.xml");

		if (!File.Exists(rootPath)) throw new FileNotFoundException("Missing Root.CPlugTree.xml", rootPath);
		if (!File.Exists(modelElementsPath)) throw new FileNotFoundException("Missing ModelElements.CPlugTree.xml", modelElementsPath);
		if (!File.Exists(leafTemplatePath)) throw new FileNotFoundException("Missing Model.CPlugTree.xml", leafTemplatePath);
		if (!File.Exists(solidPath)) warn("Template.Solid.xml not found (material refs fallback will be limited).");

		// Detect surface and visuals (after you've injected/moved generated XML into templateDir)
		var surfaceCandidates = Directory.GetFiles(templateDir, "*.CPlugSurface*.xml", SearchOption.TopDirectoryOnly)
								 .Select(Path.GetFileName)
								 .Where(x => !string.IsNullOrWhiteSpace(x))
								 .ToList()!;

		// priorité à une surface qui n'est PAS celle du template
		var surfaceXml = surfaceCandidates
			.Where(x => !x!.StartsWith("Template.", StringComparison.OrdinalIgnoreCase))
			.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault()
			?? surfaceCandidates
			.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();


		var visualCandidates = Directory.GetFiles(templateDir, "*CPlugVisualIndexedTriangles*.xml", SearchOption.TopDirectoryOnly)
								.Select(Path.GetFileName)
								.Where(x => !string.IsNullOrWhiteSpace(x))
								.ToList()!;

		var visualXmls = visualCandidates
			.Where(x => !x!.StartsWith("Model.", StringComparison.OrdinalIgnoreCase)) // on écarte le visual "dummy" du template
			.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
			.ToList();

		// fallback si jamais il n'y en a pas
		if (visualXmls.Count == 0)
		{
			visualXmls = visualCandidates.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
		}


		log($"TemplateTreeBuilder: visuals detected: {visualXmls.Count}");
		if (surfaceXml != null) log($"TemplateTreeBuilder: surface detected: {surfaceXml}");
		else warn("No surface xml found (*.CPlugSurface*.xml). Root will keep its current surface link.");

		// Material refs declared in Template.Solid.xml (refname="...")
		HashSet<string> declaredRefs = new(StringComparer.OrdinalIgnoreCase);
		string? fallbackRef = null;

		if (File.Exists(solidPath))
		{
			declaredRefs = SolidReferenceTable.GetDeclaredRefs(solidPath);
			fallbackRef = SolidReferenceTable.PickFallbackRef(declaredRefs);
			log($"TemplateTreeBuilder: declared material refs: {declaredRefs.Count} (fallback='{fallbackRef ?? "null"}')");
		}
		else
		{
			fallbackRef = "sand";
		}

		// 1) Patch Root.CPlugTree.xml : model name + surface link
		PatchRootTree(rootPath, modelName, surfaceXml, log, warn);

		// 2) Generate Part_XX trees from Model.CPlugTree.xml
		string leafTemplateText = File.ReadAllText(leafTemplatePath);

		int partsCount = visualXmls.Count;
		if (partsCount == 0)
		{
			warn("No visual XML detected. Will NOT generate Part_XX trees. ModelElements will keep its existing children.");
			return new BuildResult
			{
				RootPath = rootPath,
				ModelElementsPath = modelElementsPath,
				SurfaceXml = surfaceXml,
				VisualXmls = visualXmls
			};
		}

		var createdParts = new List<string>();

		for (int i = 0; i < partsCount; i++)
		{
			string partName = $"Part_{i:00}";
			string partFile = $"{partName}.CPlugTree.xml";
			string partPath = Path.Combine(templateDir, partFile);

			string visualFile = visualXmls[i];

			// Desired material ref from faceGroups (order mapping)
			string desiredRef = "sand";
			if (i < faceGroups.Count && !string.IsNullOrWhiteSpace(faceGroups[i].MaterialName))
				desiredRef = desiredRef = NormalizeMat(faceGroups[i].MaterialName).ToLowerInvariant();

			// Validate against declared refs; fallback if missing
			if (declaredRefs.Count > 0 && !declaredRefs.Contains(desiredRef))
			{
				var fb = fallbackRef ?? desiredRef;
				warn($"Material ref '{desiredRef}' not declared in Template.Solid.xml -> using '{fb}'");
				desiredRef = fb;
			}
			else if (declaredRefs.Count == 0 && desiredRef != (fallbackRef ?? "sand"))
			{
				// we don't know declared refs; play safe
				var fb = fallbackRef ?? "sand";
				warn($"No declared refs found; forcing material '{fb}' (wanted '{desiredRef}')");
				desiredRef = fb;
			}

			string outText = leafTemplateText;

			// Replace element name (chunk 00D lookbackstr)
			outText = ReplaceLookbackStrType40(outText, partName);

			// Replace visual link
			outText = ReplaceFirstNodeLink(outText, visualFile);

			// Replace material ref
			outText = ReplaceFirstNodeRef(outText, desiredRef);

			File.WriteAllText(partPath, outText);
			createdParts.Add(partFile);
		}

		log($"TemplateTreeBuilder: created parts: {createdParts.Count}");

		// 3) Rewrite ModelElements.CPlugTree.xml children list to point to Part_XX
		RewriteModelElementsChildren(modelElementsPath, createdParts);

		log("TemplateTreeBuilder: ModelElements children updated.");

		return new BuildResult
		{
			RootPath = rootPath,
			ModelElementsPath = modelElementsPath,
			CreatedParts = createdParts,
			SurfaceXml = surfaceXml,
			VisualXmls = visualXmls
		};
	}

	private static void PatchRootTree(string rootPath, string modelName, string? surfaceXml, Action<string> log, Action<string> warn)
	{
		string text = File.ReadAllText(rootPath);

		// Replace model name
		text = ReplaceLookbackStrType40(text, modelName);

		// Replace surface link if we detected one
		if (!string.IsNullOrWhiteSpace(surfaceXml))
		{
			// Replace first link="...CPlugSurface....xml"
			var rx = new Regex("link\\s*=\\s*\"([^\"]*CPlugSurface[^\"]*\\.xml)\"", RegexOptions.IgnoreCase);
			if (rx.IsMatch(text))
			{
				text = rx.Replace(text, $"link=\"{surfaceXml}\"", 1);
				log($"Root patched: surface link -> {surfaceXml}");
			}
			else
			{
				// fallback: replace literal Template.CPlugSurfaceCrystal.xml if present
				if (text.Contains("Template.CPlugSurfaceCrystal.xml", StringComparison.OrdinalIgnoreCase))
				{
					text = Regex.Replace(text, "Template\\.CPlugSurfaceCrystal\\.xml", surfaceXml, RegexOptions.IgnoreCase);
					log($"Root patched (literal): surface link -> {surfaceXml}");
				}
				else
				{
					warn("Root patch: could not find a surface link node to replace (kept as-is).");
				}
			}
		}

		File.WriteAllText(rootPath, text);
	}

	private static void RewriteModelElementsChildren(string modelElementsPath, List<string> partFiles)
	{
		string text = File.ReadAllText(modelElementsPath);

		var lines = new List<string>();
		lines.Add("            <list>  <!-- Children -->");
		foreach (var f in partFiles)
		{
			lines.Add("                <element>");
			lines.Add($"                    <node link=\"{EscapeXml(f)}\"/>");
			lines.Add("                </element>");
		}
		lines.Add("            </list>");

		string newList = string.Join(Environment.NewLine, lines);

		// Replace first <list>...</list>
		var rx = new Regex(@"(?s)<list\b[^>]*>.*?</list>", RegexOptions.IgnoreCase);
		text = rx.Replace(text, newList, 1);

		File.WriteAllText(modelElementsPath, text);
	}

	private static string ReplaceLookbackStrType40(string xml, string value)
	{
		var rx = new Regex(@"(?s)<lookbackstr\s+type\s*=\s*""40"">\s*.*?\s*</lookbackstr>", RegexOptions.IgnoreCase);
		return rx.Replace(xml, $"<lookbackstr type=\"40\">{EscapeXml(value)}</lookbackstr>", 1);
	}

	private static string ReplaceFirstNodeLink(string xml, string fileName)
	{
		var rx = new Regex(@"<node\s+link\s*=\s*""[^""]+""\s*/>", RegexOptions.IgnoreCase);
		return rx.Replace(xml, $"<node link=\"{EscapeXml(fileName)}\"/>", 1);
	}

	private static string ReplaceFirstNodeRef(string xml, string refName)
	{
		var rx = new Regex(@"<node\s+ref\s*=\s*""[^""]+""\s*/>", RegexOptions.IgnoreCase);
		return rx.Replace(xml, $"<node ref=\"{EscapeXml(refName)}\"/>", 1);
	}
	private static string NormalizeMat(string name)
	{
		if (string.IsNullOrWhiteSpace(name)) return "Concrete";

		name = name.Trim();

		// alias fréquents
		return name.ToLowerInvariant() switch
		{
			"dirt" => "Dirt",
			"grass" => "Grass",
			"metal" => "Metal",
			"sand" => "Sand",
			"ice" => "Ice",
			"water" => "Water",
			_ => char.ToUpperInvariant(name[0]) + name.Substring(1) // dirt -> Dirt
		};
	}

	private static string EscapeXml(string s)
	{
		return s
			.Replace("&", "&amp;")
			.Replace("<", "&lt;")
			.Replace(">", "&gt;")
			.Replace("\"", "&quot;")
			.Replace("'", "&apos;");
	}
}
