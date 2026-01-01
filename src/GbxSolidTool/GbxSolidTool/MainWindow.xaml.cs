using HelixToolkit.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using GbxSolidTool.Core;

namespace GbxSolidTool
{
	public partial class MainWindow : Window
	{
		private bool _wireframeEnabled;
		private readonly ModelImporter _importer = new();
		private ModelVisual3D? _modelVisual;
		private LinesVisual3D? _wireframe;
		private Color _overrideColor = Colors.LightGray;
		private readonly AppPaths _paths = new(); 
		private bool _logsVisible = true;
		private GridLength _lastLogsWidth = new GridLength(380);


		public MainWindow()
		{
			InitializeComponent();

			// Lights (sinon écran noir)
			View3D.Children.Add(new DefaultLights());

			Log("Ready.");
			UpdateOverlay();
		}
		private void LoadTemplate_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				// Repo root / tools
				var tools3ds2gbxml = Path.Combine(_paths.RepoRoot, "tools", "3ds2gbxml");
				if (!Directory.Exists(tools3ds2gbxml))
				{
					Status("tools/3ds2gbxml missing.");
					Log($"Missing folder: {tools3ds2gbxml}");
					return;
				}

				// Cherche tous les .3ds
				var candidates = Directory.GetFiles(tools3ds2gbxml, "*.3ds", SearchOption.AllDirectories);
				if (candidates.Length == 0)
				{
					Status("No template .3ds found in tools/3ds2gbxml.");
					Log("No *.3ds found under: " + tools3ds2gbxml);
					return;
				}

				// Score: on préfère "Template.3ds", puis ceux contenant "template" ou "basic"
				string PickBest(string[] files)
				{
					int Score(string p)
					{
						var name = Path.GetFileName(p).ToLowerInvariant();
						var full = p.ToLowerInvariant();
						int s = 0;

						if (name == "template.3ds") s += 1000;
						if (name.Contains("template")) s += 200;
						if (full.Contains("basic")) s += 150;
						if (full.Contains("model")) s += 50;

						// plus près de la racine = un peu mieux
						var depth = p.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
						s -= depth;

						return s;
					}

					return files.OrderByDescending(Score).First();
				}

				var best = PickBest(candidates);

				Log($"Load Template: {best}");
				LoadModel(best);
			}
			catch (Exception ex)
			{
				Log("LoadTemplate ERROR: " + ex);
				Status("Load template failed (see logs).");
			}
		}
		private void Open_Click(object sender, RoutedEventArgs e)
		{
			var dlg = new OpenFileDialog
			{
				Filter = "3DS model (*.3ds)|*.3ds",
				DefaultExt = ".3ds",
				AddExtension = true,
				CheckFileExists = true,
				Title = "Open a 3D model"
			};

			if (dlg.ShowDialog() == true)
				LoadModel(dlg.FileName);
		}

		private void Clear_Click(object sender, RoutedEventArgs e)
		{
			RemoveCurrentModel();
			Status("Cleared.");
		}
		private void UpdateOverlay()
		{
			// overlay visible uniquement si aucun modèle
			DropOverlay.Visibility = (_modelVisual == null) ? Visibility.Visible : Visibility.Collapsed;
		}
		private void ToggleLogs_Click(object sender, RoutedEventArgs e)
		{
			SetLogsVisible(!_logsVisible);
		}

		private void SetLogsVisible(bool visible)
		{
			_logsVisible = visible;

			if (visible)
			{
				// restaure largeur précédente (ou 380 par défaut)
				LogsColumn.Width = (_lastLogsWidth.Value < 1) ? new GridLength(380) : _lastLogsWidth;
				LogsColumn.MinWidth = 220;
			}
			else
			{
				// mémorise largeur actuelle, puis cache
				_lastLogsWidth = LogsColumn.Width;
				LogsColumn.MinWidth = 0;
				LogsColumn.Width = new GridLength(0);
			}
			ToggleLogsButton.Content = visible ? "Hide Logs" : "Show Logs";

		}

		private void Viewport_DragOver(object sender, DragEventArgs e)
		{
			e.Effects = DragDropEffects.None;

			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				var files = (string[])e.Data.GetData(DataFormats.FileDrop);
				if (files?.Length > 0 && File.Exists(files[0]))
					e.Effects = DragDropEffects.Copy;
				if (files?.Length > 0 && File.Exists(files[0]) &&
					string.Equals(Path.GetExtension(files[0]), ".3ds", StringComparison.OrdinalIgnoreCase))
				{
					e.Effects = DragDropEffects.Copy;
				}

			}

			e.Handled = true;
		}

		private void Viewport_Drop(object sender, DragEventArgs e)
		{
			if (!e.Data.GetDataPresent(DataFormats.FileDrop))
				return;

			var files = (string[])e.Data.GetData(DataFormats.FileDrop);
			if (files == null || files.Length == 0)
				return;

			var path = files[0];
			if (!string.Equals(Path.GetExtension(path), ".3ds", StringComparison.OrdinalIgnoreCase))
			{
				Status("Only .3ds is supported.");
				return;
			}

			if (File.Exists(path))
				LoadModel(path);


		}

		private void Wireframe_Checked(object sender, RoutedEventArgs e)
		{
			_wireframeEnabled = true;
			UpdateWireframe(true);
		}

		private void Wireframe_Unchecked(object sender, RoutedEventArgs e)
		{
			_wireframeEnabled = false;
			UpdateWireframe(false);
		}

		private void ColorCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
		{
			var selected = (e.AddedItems.Count > 0) ? e.AddedItems[0]?.ToString() : null;
			// ComboBoxItem.ToString() => "System.Windows.Controls.ComboBoxItem: LightGray"
			var name = selected?.Split(':').LastOrDefault()?.Trim();

			_overrideColor = name switch
			{
				"White" => Colors.White,
				"Orange" => Colors.Orange,
				"HotPink" => Colors.HotPink,
				"Cyan" => Colors.Cyan,
				"Lime" => Colors.Lime,
				_ => Colors.LightGray
			};

			ApplyOverrideMaterial();
		}

		private void LoadModel(string path)
		{
			try
			{
				if (!File.Exists(path))
				{
					Status($"File not found: {path}");
					return;
				}

				Status($"Loading: {path}");
				Log($"Load: {path}");

				RemoveCurrentModel();

				// Import
				var model = _importer.Load(path);

				// Override material color (simple + stable)
				OverrideMaterialsRecursive(model, MaterialHelper.CreateMaterial(_overrideColor));

				_modelVisual = new ModelVisual3D { Content = model };
				View3D.Children.Add(_modelVisual);

				// Fit view
				View3D.ZoomExtents();

				Status($"Loaded: {Path.GetFileName(path)} ({path})");

				UpdateOverlay();

				// If wireframe is checked, rebuild it
				UpdateWireframe(_wireframeEnabled);

			}
			catch (Exception ex)
			{
				Status("Load failed (see logs).");
				Log("ERROR: " + ex);
				MessageBox.Show(ex.Message, "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void RemoveCurrentModel()
		{
			if (_wireframe != null)
			{
				View3D.Children.Remove(_wireframe);
				_wireframe = null;
			}

			if (_modelVisual != null)
			{
				View3D.Children.Remove(_modelVisual);
				_modelVisual = null;
			}
			UpdateOverlay();
		}

		private bool IsWireframeChecked() => _wireframeEnabled;

		private void UpdateWireframe(bool enabled)
		{
			try
			{
				if (!enabled)
				{
					if (_wireframe != null)
					{
						View3D.Children.Remove(_wireframe);
						_wireframe = null;
						Status("Wireframe OFF");
					}
					return;
				}

				if (_modelVisual?.Content == null)
				{
					Status("Wireframe: no model loaded.");
					return;
				}

				// Rebuild from model geometry
				var points = WireframeBuilder.BuildLinePoints(_modelVisual.Content);

				if (_wireframe != null)
					View3D.Children.Remove(_wireframe);

				_wireframe = new LinesVisual3D
				{
					Thickness = 1.0,
					Color = Colors.White,
					Points = new Point3DCollection(points)
				};

				View3D.Children.Add(_wireframe);
				Status("Wireframe ON");
			}
			catch (Exception ex)
			{
				Log("Wireframe ERROR: " + ex);
				Status("Wireframe failed (see logs).");
			}
		}

		private void ApplyOverrideMaterial()
		{
			if (_modelVisual?.Content == null)
				return;

			try
			{
				OverrideMaterialsRecursive(_modelVisual.Content, MaterialHelper.CreateMaterial(_overrideColor));
				Status($"Color: {_overrideColor}");
			}
			catch (Exception ex)
			{
				Log("Color override ERROR: " + ex);
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

		private void Status(string text) => StatusText.Text = text;

		private void Log(string text)
		{
			LogBox.AppendText(text + Environment.NewLine);
			LogBox.ScrollToEnd();
		}

		/// <summary>
		/// Minimal wireframe builder: extracts triangle edges from MeshGeometry3D
		/// and returns a list of points pairs (A,B,A,B...) for LinesVisual3D.
		/// </summary>
		private static class WireframeBuilder
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

				// Deduplicate edges: store (minIndex,maxIndex)
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
	}
}
