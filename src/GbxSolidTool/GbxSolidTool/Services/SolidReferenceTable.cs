using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GbxSolidTool.Services;

public static class SolidReferenceTable
{
	public static HashSet<string> GetDeclaredRefs(string templateSolidXmlPath)
	{
		var text = File.ReadAllText(templateSolidXmlPath);

		var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Match m in Regex.Matches(text, "refname\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
		{
			var r = m.Groups[1].Value.Trim();
			if (!string.IsNullOrWhiteSpace(r))
				refs.Add(r);
		}

		return refs;
	}

	public static string? PickFallbackRef(HashSet<string> declaredRefs)
	{
		if (declaredRefs.Count == 0) return null;

		var sand = declaredRefs.FirstOrDefault(r => r.Equals("sand", StringComparison.OrdinalIgnoreCase));
		if (sand != null) return sand;

		return declaredRefs.First();
	}
}
