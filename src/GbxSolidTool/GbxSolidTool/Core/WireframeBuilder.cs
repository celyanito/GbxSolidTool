using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;

namespace GbxSolidTool.Core;

public static class WireframeBuilder
{
	public static List<Point3D> BuildLinePoints(Model3D model)
	{
		var lines = new List<Point3D>();
		AddModelLines(model, Transform3D.Identity, lines);
		return lines;
	}

	private static void AddModelLines(Model3D model, Transform3D parent, List<Point3D> outLines)
	{
		var current = Combine(parent, model.Transform);

		if (model is Model3DGroup g)
		{
			foreach (var child in g.Children)
				AddModelLines(child, current, outLines);
			return;
		}

		if (model is GeometryModel3D gm && gm.Geometry is MeshGeometry3D mesh)
		{
			AddMeshEdges(mesh, current, outLines);
		}
	}

	private static void AddMeshEdges(MeshGeometry3D mesh, Transform3D transform, List<Point3D> outLines)
	{
		if (mesh.Positions == null || mesh.TriangleIndices == null || mesh.TriangleIndices.Count < 3)
			return;

		var edges = new HashSet<(int a, int b)>();
		var idx = mesh.TriangleIndices;

		for (int i = 0; i + 2 < idx.Count; i += 3)
		{
			AddEdge(idx[i], idx[i + 1]);
			AddEdge(idx[i + 1], idx[i + 2]);
			AddEdge(idx[i + 2], idx[i]);
		}

		void AddEdge(int i0, int i1)
		{
			if (i0 == i1) return;
			var a = Math.Min(i0, i1);
			var b = Math.Max(i0, i1);
			edges.Add((a, b));
		}

		foreach (var (a, b) in edges)
		{
			if (a < 0 || b < 0 || a >= mesh.Positions.Count || b >= mesh.Positions.Count)
				continue;

			var p0 = transform.Transform(mesh.Positions[a]);
			var p1 = transform.Transform(mesh.Positions[b]);
			outLines.Add(p0);
			outLines.Add(p1);
		}
	}

	private static Transform3D Combine(Transform3D a, Transform3D b)
	{
		if (a == null || a == Transform3D.Identity) return b ?? Transform3D.Identity;
		if (b == null || b == Transform3D.Identity) return a;

		var tg = new Transform3DGroup();
		tg.Children.Add(a);
		tg.Children.Add(b);
		return tg;
	}
}
