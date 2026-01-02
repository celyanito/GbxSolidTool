using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using GbxSolidTool.Core;
using GbxSolidTool.Models;

namespace GbxSolidTool.Services;

public sealed class ViewportService
{
	private readonly HelixViewport3D _view;
	private ModelVisual3D? _modelVisual;
	private LinesVisual3D? _wireframe;
	private bool _lightsAdded;

	public ViewportService(HelixViewport3D view)
	{
		_view = view;
	}

	public bool HasModel => _modelVisual?.Content != null;

	public void EnsureLights()
	{
		if (_lightsAdded) return;
		_view.Children.Add(new DefaultLights());
		_lightsAdded = true;
	}

	public void Clear()
	{
		if (_wireframe != null)
		{
			_view.Children.Remove(_wireframe);
			_wireframe = null;
		}
		if (_modelVisual != null)
		{
			_view.Children.Remove(_modelVisual);
			_modelVisual = null;
		}
	}

	public void ShowModel(Model3D model)
	{
		Clear();
		_modelVisual = new ModelVisual3D { Content = model };
		_view.Children.Add(_modelVisual);
		_view.ZoomExtents();
	}

	public void UpdateWireframe(bool enabled)
	{
		if (!enabled)
		{
			if (_wireframe != null)
			{
				_view.Children.Remove(_wireframe);
				_wireframe = null;
			}
			return;
		}

		if (_modelVisual?.Content == null)
			return;

		var points = WireframeBuilder.BuildLinePoints(_modelVisual.Content);

		if (_wireframe != null)
			_view.Children.Remove(_wireframe);

		_wireframe = new LinesVisual3D
		{
			Thickness = 1.0,
			Color = Colors.White,
			Points = new Point3DCollection(points)
		};

		_view.Children.Add(_wireframe);
	}

	public void ApplyOverrideColor(Color color)
	{
		if (_modelVisual?.Content == null) return;
		OverrideMaterialsRecursive(_modelVisual.Content, MaterialHelper.CreateMaterial(color));
	}

	public static void CollectGeometryModels(Model3D model, List<GeometryModel3D> list)
	{
		if (model is GeometryModel3D gm)
		{
			list.Add(gm);
			return;
		}

		if (model is Model3DGroup grp)
		{
			foreach (var child in grp.Children)
				CollectGeometryModels(child, list);
		}
	}

	public static void ApplyFaceGroupColors(List<GeometryModel3D> geoms, List<FaceMatGroup> faceGroups, Color fallbackColor, Action<Color, string>? warn = null)
	{
		if (geoms.Count != faceGroups.Count)
			warn?.Invoke(Colors.Orange, $"WARN: Helix geoms={geoms.Count} vs 3DS groups={faceGroups.Count} (mapping par ordre)");

		int n = Math.Min(geoms.Count, faceGroups.Count);

		for (int i = 0; i < n; i++)
		{
			var matName = faceGroups[i].MaterialName;

			var color = MaterialCatalog.IsKnown(matName)
				? MaterialCatalog.GetMaterialColor(matName)
				: Colors.Red;

			var mat = MaterialHelper.CreateMaterial(color);
			geoms[i].Material = mat;
			geoms[i].BackMaterial = mat;
		}

		if (geoms.Count > n)
		{
			var fallback = MaterialHelper.CreateMaterial(fallbackColor);
			for (int i = n; i < geoms.Count; i++)
			{
				geoms[i].Material = fallback;
				geoms[i].BackMaterial = fallback;
			}
		}
	}

	private static void OverrideMaterialsRecursive(Model3D model, Material mat)
	{
		if (model is Model3DGroup g)
		{
			foreach (var child in g.Children)
				OverrideMaterialsRecursive(child, mat);
			return;
		}

		if (model is GeometryModel3D gm)
		{
			gm.Material = mat;
			gm.BackMaterial = mat;
		}
	}
}
