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
using System.Threading.Tasks;

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
		private readonly ProcessRunner _runner = new();
		private string? _currentModelPath;
		private string? _currentWorkDir;


		public MainWindow()
		{
			InitializeComponent();

			// Lights (sinon écran noir)
			View3D.Children.Add(new DefaultLights());

			Log("Ready.");
			UpdateOverlay();
		}
		private string PrepareTemplateForGbxc()
		{
			if (_currentWorkDir == null)
				throw new InvalidOperationException("Work directory not prepared.");

			// Search Template.Solid.xml anywhere under tools/3ds2gbxml
			var baseDir = Path.Combine(_paths.RepoRoot, "tools", "3ds2gbxml");
			if (!Directory.Exists(baseDir))
				throw new DirectoryNotFoundException($"Missing: {baseDir}");

			var solidPath = Directory.GetFiles(baseDir, "Template.Solid.xml", SearchOption.AllDirectories)
									 .FirstOrDefault();

			if (solidPath == null)
				throw new FileNotFoundException($"Template.Solid.xml not found under: {baseDir}");

			// This is the "Basic Model" folder (or whatever it is called) that contains Template.Solid.xml
			var srcTemplateRoot = Path.GetDirectoryName(solidPath)!;

			var dstTemplate = Path.Combine(_currentWorkDir, "template");

			if (Directory.Exists(dstTemplate))
				Directory.Delete(dstTemplate, recursive: true);

			Directory.CreateDirectory(dstTemplate);

			// Copy CONTENT of srcTemplateRoot into work/.../template/
			CopyDirectoryContents(srcTemplateRoot, dstTemplate);

			Log($"Template root: {srcTemplateRoot}");
			Log($"Template copied: {dstTemplate}");

			var solid = Path.Combine(dstTemplate, "Template.Solid.xml");
			Log(File.Exists(solid) ? "Template.Solid.xml OK" : "WARNING: Template.Solid.xml missing after copy");

			return dstTemplate;
		}

		private static void CopyDirectoryContents(string sourceDir, string destinationDir)
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
				CopyDirectory(dir, dst); // tu as déjà CopyDirectory(source, dest)
			}
		}


		private void InjectXmlIntoTemplate(string templateDir)
		{
			var xmlDir = Path.Combine(_currentWorkDir!, "xml");
			if (!Directory.Exists(xmlDir))
				throw new DirectoryNotFoundException($"XML folder not found: {xmlDir}");

			foreach (var src in Directory.GetFiles(xmlDir, "*.xml"))
			{
				var dst = Path.Combine(templateDir, Path.GetFileName(src));
				File.Copy(src, dst, overwrite: true);
			}
		}

		private static void CopyDirectory(string sourceDir, string destinationDir)
		{
			Directory.CreateDirectory(destinationDir);

			foreach (var file in Directory.GetFiles(sourceDir))
				File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);

			foreach (var dir in Directory.GetDirectories(sourceDir))
				CopyDirectory(dir, Path.Combine(destinationDir, Path.GetFileName(dir)));
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
		private async void ConvertXml_Click(object sender, RoutedEventArgs e)
		{
			if (_currentModelPath == null)
			{
				Status("No model loaded.");
				return;
			}

			if (!_paths.HasTools(out var msg))
			{
				Status("Tools missing.");
				Log(msg);
				return;
			}

			// Prépare work\<Model>\{input,xml,build} et copie le .3ds dans input\
			var work3ds = PrepareWorkDirForModel(_currentModelPath);

			// Flags depuis UI
			var flags = new List<string>();
			if (Cb3dsVisual.IsChecked == true) flags.Add("-v");
			if (Cb3dsSurface.IsChecked == true) flags.Add("-s");

			var args = $"\"{_paths.ThreeDs2Gbxml}\" {string.Join(" ", flags)} \"{work3ds}\"";

			Log("");
			Log("=== 3ds2gbxml ===");
			Log($"WORK: {_currentWorkDir}");
			Log($"CWD : {_currentWorkDir}");
			Log($"CMD : {_paths.PythonExe} {args}");
			Status("Converting to XML...");

			int code = await _runner.RunAsync(
				_paths.PythonExe,
				args,
				s => Dispatcher.Invoke(() => Log(s)),
				s => Dispatcher.Invoke(() => Log("ERR: " + s)),
				workingDirectory: _currentWorkDir
			);

			Log($"ExitCode: {code}");

			if (code != 0)
			{
				Status($"XML failed (exit {code})");
				return;
			}

			CollectXmlOutputs(_currentWorkDir!);
			Status($"XML OK: {_currentWorkDir}\\xml");
		}

		private async void CompileGbx_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				if (_currentModelPath == null)
				{
					Status("No model loaded.");
					return;
				}

				if (!_paths.HasTools(out var msg))
				{
					Status("Tools missing.");
					Log(msg);
					return;
				}

				if (_currentWorkDir == null)
				{
					Status("No work folder yet. Run Convert XML first.");
					return;
				}

				var modelName = Path.GetFileNameWithoutExtension(_currentModelPath);

				Log("");
				Log("=== GBXC (template workflow) ===");
				Log($"WorkDir: {_currentWorkDir}");

				// 1) Prepare template folder (copy Basic Model CONTENT into work/.../template/)
				Status("Preparing template...");
				var templateDir = PrepareTemplateForGbxc();
				Log($"TemplateDir: {templateDir}");

				// 2) Inject generated XML into template
				Status("Injecting XML...");
				InjectXmlIntoTemplate(templateDir);

				// 3) Solid XML path
				var solidXml = Path.Combine(templateDir, "Template.Solid.xml");
				Log($"SolidXml: {solidXml}");
				if (!File.Exists(solidXml))
				{
					Status("Template.Solid.xml not found in work/template.");
					Log($"Missing: {solidXml}");
					return;
				}

				// 4) Output
				var buildDir = Path.Combine(_currentWorkDir, "build");
				Directory.CreateDirectory(buildDir);
				var outGbx = Path.Combine(buildDir, $"{modelName}.Solid.gbx");

				// 5) Run GBXC
				var args = $"\"{_paths.GbxcMain}\" -v -o \"{outGbx}\" \"{solidXml}\"";

				Log($"PY : {_paths.PythonExe}");
				Log($"CWD: {templateDir}");
				Log($"CMD: {_paths.PythonExe} {args}");
				Status("Compiling GBX...");

				int code = await _runner.RunAsync(
					_paths.PythonExe,
					args,
					s => Dispatcher.Invoke(() => Log(s)),
					s => Dispatcher.Invoke(() => Log("ERR: " + s)),
					workingDirectory: templateDir
				);

				Log($"ExitCode: {code}");

				if (code != 0)
				{
					Status($"GBX failed (exit {code})");
					return;
				}

				Status($"GBX OK: {outGbx}");
			}
			catch (Exception ex)
			{
				Log("CompileGbx_Click CRASH: " + ex);
				Status("Compile crashed (see logs).");
				MessageBox.Show(ex.ToString(), "Compile crash", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}


		private void CollectXmlOutputs(string workDir)
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

			// On prend tous les XML sous workDir, sauf ceux déjà dans workDir\xml\
			var xmlFiles = Directory.GetFiles(workDir, "*.xml", SearchOption.AllDirectories)
									.Where(p => !IsInsideXmlFolder(p))
									.ToList();

			int moved = 0;

			foreach (var src in xmlFiles)
			{
				var dst = Path.Combine(xmlTarget, Path.GetFileName(src));

				try
				{
					// Si destination existe déjà, on l’écrase
					if (File.Exists(dst))
						File.Delete(dst);

					// Move (pas Copy) => plus de doublon
					File.Move(src, dst);
					moved++;
				}
				catch (Exception ex)
				{
					// fallback: si move échoue (verrou, etc.), on copie puis on essaie de supprimer
					Log($"WARN: move failed for {src} -> {dst} ({ex.Message}), trying copy+delete");
					File.Copy(src, dst, overwrite: true);
					try { File.Delete(src); } catch { /* ignore */ }
					moved++;
				}
			}

			// Nettoyage: supprime dossiers vides (optionnel)
			TryDeleteEmptyFolders(workDir, xmlTarget);

			Log($"Collected XML: moved {moved} file(s) into: {xmlTarget}");
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

					// ne supprime pas build/input/xml
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
				// nettoyage best-effort, on ne bloque pas
			}
		}


		// Variante "unique" (si tu veux un dossier différent à chaque run)
		private string PrepareWorkDirForModel(string source3dsPath)
		{
			var name = Path.GetFileNameWithoutExtension(source3dsPath);
			var workRoot = Path.Combine(_paths.RepoRoot, "work");
			Directory.CreateDirectory(workRoot);

			var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			var projectDir = Path.Combine(workRoot, $"{name}_{stamp}");

			var inputDir = Path.Combine(projectDir, "input");
			var xmlDir   = Path.Combine(projectDir, "xml");
			var buildDir = Path.Combine(projectDir, "build");

			Directory.CreateDirectory(inputDir);
			Directory.CreateDirectory(xmlDir);
			Directory.CreateDirectory(buildDir);

			var dst3ds = Path.Combine(inputDir, Path.GetFileName(source3dsPath));
			File.Copy(source3dsPath, dst3ds, overwrite: true);

			_currentWorkDir = projectDir;
			return dst3ds;
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
				_currentModelPath = path;


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
