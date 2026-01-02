using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using GbxSolidTool.Core;
using GbxSolidTool.Models;

namespace GbxSolidTool.Services;

public static class MaterialLogging
{
	private sealed class MaterialFaceInfo
	{
		public string Name { get; init; } = "UNKNOWN";
		public int FaceCount { get; init; }
	}

	public static void LogDetectedMaterialsWithFaces(
		Action<string> log,
		Action<Color, string> logColor,
		List<string> declaredMaterials,
		List<FaceMatGroup> faceGroups)
	{
		var facesPerMaterial = faceGroups
			.Where(g => !string.IsNullOrWhiteSpace(g.MaterialName))
			.GroupBy(g => g.MaterialName, StringComparer.OrdinalIgnoreCase)
			.Select(g => new MaterialFaceInfo
			{
				Name = g.Key,
				FaceCount = g.Sum(x => x.FaceCount)
			})
			.OrderByDescending(x => x.FaceCount)
			.ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		var usedMaterials = facesPerMaterial.Select(x => x.Name).ToList();

		log($"3DS materials (declared 0xA000): {declaredMaterials.Count}");
		log($"3DS materials (used 0x4130): {usedMaterials.Count}");

		// Règle TM: A000 ne doit PAS être présent
		if (declaredMaterials.Count > 0)
		{
			logColor(Colors.Orange,
				$"⚠ WARNING: {declaredMaterials.Count} material block(s) (0xA000) detected.\n" +
				"  TrackMania requires NO A000 blocks.\n" +
				"  Re-export the model using GreffMASTER's Blender addon.");
		}

		log("Matériaux détectés :");

		foreach (var m in facesPerMaterial)
		{
			if (MaterialCatalog.IsKnown(m.Name))
			{
				var col = MaterialCatalog.GetMaterialColor(m.Name);
				logColor(col, $"- {m.Name} ({m.FaceCount} faces)");
			}
			else
			{
				logColor(Colors.Red, $"- {m.Name} ({m.FaceCount} faces)  [UNKNOWN MATERIAL]");
			}
		}

		var unknown = facesPerMaterial.Where(x => !MaterialCatalog.IsKnown(x.Name)).Select(x => x.Name).ToList();
		if (unknown.Count > 0)
			logColor(Colors.Red, $"⚠ Matériaux inconnus : {unknown.Count}");
	}
}
