using GbxSolidTool.Core;
using GbxSolidTool.Models;
using GbxSolidTool.Services;
using HelixToolkit.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace GbxSolidTool
{
	public partial class MainWindow : Window
	{
		private bool _wireframeEnabled;
		private readonly ModelImporter _importer = new();
		private Color _overrideColor = Colors.LightGray;

		private readonly AppPaths _paths = new();
		private readonly ProcessRunner _runner = new();

		private string? _currentModelPath;
		private string? _currentWorkDir;
		private string? _currentTemplateDir;
		private string? _lastErrLine;

		private bool _logsVisible = true;
		private GridLength _lastLogsWidth = new GridLength(380);

		private readonly ViewportService _viewport;
		private readonly WorkDirService _work;
		private readonly TemplateService _template;
		
		private int _lastErrCount;
		private bool _normalsEnabled;
		private LinesVisual3D? _normalsBlue;
		private LinesVisual3D? _normalsRed;
		private Model3D? _currentModel;
		private bool _faceOrientationEnabled;

		// backup des matériaux pour pouvoir restaurer quand on décoche
		private readonly Dictionary<GeometryModel3D, (Material? Front, Material? Back)> _savedFaceMats = new();

		public MainWindow()
		{
			InitializeComponent();

			_viewport = new ViewportService(View3D);
			_viewport.EnsureLights();

			_work = new WorkDirService(_paths, s => Log(s));
			_template = new TemplateService(_paths, s => Log(s));

			Log("Ready.");
			UpdateOverlay();
		}
		private void ApplyFaceOrientationMode()
		{
			if (_currentModel == null) return;

			var geoms = new List<GeometryModel3D>();
			ViewportService.CollectGeometryModels(_currentModel, geoms);

			// matériaux “Blender-like”
			var frontMat = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(190, 80, 140, 255))); // bleu
			var backMat = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(190, 255, 80, 80)));  // rouge

			foreach (var g in geoms)
			{
				// sauvegarde une seule fois
				if (!_savedFaceMats.ContainsKey(g))
					_savedFaceMats[g] = (g.Material, g.BackMaterial);

				g.Material = frontMat;
				g.BackMaterial = backMat;
			}

			Log("Face Orientation: ON (blue front / red back)");
		}

		private void RestoreFaceOrientationMode()
		{
			foreach (var kv in _savedFaceMats)
			{
				kv.Key.Material = kv.Value.Front;
				kv.Key.BackMaterial = kv.Value.Back;
			}

			Log("Face Orientation: OFF (restored materials)");
		}


		// ---------------- UI helpers ----------------
		private void NormalsCheckBox_Checked(object sender, RoutedEventArgs e)
		{
			_faceOrientationEnabled = true;
			ApplyFaceOrientationMode();
		}

		private void NormalsCheckBox_Unchecked(object sender, RoutedEventArgs e)
		{
			_faceOrientationEnabled = false;
			RestoreFaceOrientationMode();
		}

		private void ClearNormalsOverlay()
		{
			if (View3D == null) return;

			if (_normalsBlue != null) View3D.Children.Remove(_normalsBlue);
			if (_normalsRed != null) View3D.Children.Remove(_normalsRed);

			_normalsBlue = null;
			_normalsRed = null;
		}

		private void UpdateNormalsOverlay()
		{
			if (View3D == null) return;

			// Si désactivé ou pas de modèle -> on nettoie
			// Si désactivé ou pas de modèle -> on nettoie
			if (!_normalsEnabled || _currentModel == null)
			{
				ClearNormalsOverlay();
				return;
			}


			// Rebuild à chaque fois (simple + fiable)
			ClearNormalsOverlay();

			var bluePts = new Point3DCollection();
			var redPts = new Point3DCollection();

			// Récupère toutes les meshes du modèle chargé
			var meshes = new List<MeshGeometry3D>();
			CollectMeshes(_currentModel, meshes);

			if (meshes.Count == 0)
				return;

			int blueSegs = 0, redSegs = 0;

			foreach (var mesh in meshes)
			{
				if (mesh.Positions == null || mesh.Positions.Count == 0)
					continue;

				if (mesh.TriangleIndices == null || mesh.TriangleIndices.Count < 3)
					continue;

				var bounds = mesh.Bounds;
				var center = new Point3D(
					bounds.X + bounds.SizeX * 0.5,
					bounds.Y + bounds.SizeY * 0.5,
					bounds.Z + bounds.SizeZ * 0.5
				);

				double size = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
				double len = Math.Max(0.001, size * 0.06); // un peu plus grand pour bien voir

				// échantillonnage si énorme
				int triCount = mesh.TriangleIndices.Count / 3;
				int stepTri = triCount > 50000 ? 10 : 1;

				for (int t = 0; t < triCount; t += stepTri)
				{
					int i0 = mesh.TriangleIndices[t * 3 + 0];
					int i1 = mesh.TriangleIndices[t * 3 + 1];
					int i2 = mesh.TriangleIndices[t * 3 + 2];

					if (i0 < 0 || i1 < 0 || i2 < 0 ||
						i0 >= mesh.Positions.Count || i1 >= mesh.Positions.Count || i2 >= mesh.Positions.Count)
						continue;

					var p0 = mesh.Positions[i0];
					var p1 = mesh.Positions[i1];
					var p2 = mesh.Positions[i2];

					// centre de face
					var pc = new Point3D(
						(p0.X + p1.X + p2.X) / 3.0,
						(p0.Y + p1.Y + p2.Y) / 3.0,
						(p0.Z + p1.Z + p2.Z) / 3.0
					);

					// normale de face via cross
					var v1 = p1 - p0;
					var v2 = p2 - p0;
					var n = Vector3D.CrossProduct(v1, v2);

					if (n.LengthSquared < 1e-12)
						continue;

					n.Normalize();

					var end = new Point3D(pc.X + n.X * len, pc.Y + n.Y * len, pc.Z + n.Z * len);

					var vc = pc - center;
					double d = (n.X * vc.X) + (n.Y * vc.Y) + (n.Z * vc.Z);

					if (d >= 0)
					{
						bluePts.Add(pc);
						bluePts.Add(end);
						blueSegs++;
					}
					else
					{
						redPts.Add(pc);
						redPts.Add(end);
						redSegs++;
					}
				}
			}

			// petit log utile
			Log($"Normals overlay: blue={blueSegs}, red={redSegs}");

		}
		private static void CollectMeshes(Model3D model, List<MeshGeometry3D> outMeshes)
		{
			if (model is Model3DGroup group)
			{
				foreach (var child in group.Children)
					CollectMeshes(child, outMeshes);
			}
			else if (model is GeometryModel3D geomModel)
			{
				if (geomModel.Geometry is MeshGeometry3D mesh)
					outMeshes.Add(mesh);
			}
		}

		private void Status(string text) => StatusText.Text = text;

		private void ClearLogs()
		{
			if (LogBox.Document != null)
				LogBox.Document.Blocks.Clear();
		}

		private void LogOk(string text) => Log(Colors.LimeGreen, "✓ " + text);
		private void LogErr(string text) => Log(Colors.Red, "✗ " + text);
		private void LogWarn(string text) => Log(Colors.Orange, "! " + text);

		private Paragraph EnsureLogParagraph()
		{
			if (LogBox.Document == null)
				LogBox.Document = new FlowDocument();

			var p = LogBox.Document.Blocks.LastBlock as Paragraph;
			if (p == null)
			{
				p = new Paragraph { Margin = new Thickness(0) };
				LogBox.Document.Blocks.Add(p);
			}
			return p;
		}

		private void Log(string text)
		{
			var p = EnsureLogParagraph();
			p.Inlines.Add(new Run(text));
			p.Inlines.Add(new LineBreak());
			LogBox.ScrollToEnd();
		}

		private void Log(Color color, string text)
		{
			var p = EnsureLogParagraph();
			p.Inlines.Add(new Run(text) { Foreground = new SolidColorBrush(color) });
			p.Inlines.Add(new LineBreak());
			LogBox.ScrollToEnd();
		}

		private void UpdateOverlay()
		{
			DropOverlay.Visibility = _viewport.HasModel ? Visibility.Collapsed : Visibility.Visible;
		}

		private void SetLogsVisible(bool visible)
		{
			_logsVisible = visible;

			if (visible)
			{
				LogsColumn.Width = (_lastLogsWidth.Value < 1) ? new GridLength(380) : _lastLogsWidth;
				LogsColumn.MinWidth = 220;
			}
			else
			{
				_lastLogsWidth = LogsColumn.Width;
				LogsColumn.MinWidth = 0;
				LogsColumn.Width = new GridLength(0);
			}

			ToggleLogsButton.Content = visible ? "Hide Logs" : "Show Logs";
		}

		// ---------------- UI events ----------------

		private void LoadTemplate_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var tools3ds2gbxml = Path.Combine(_paths.RepoRoot, "tools", "3ds2gbxml");
				if (!Directory.Exists(tools3ds2gbxml))
				{
					Status("tools/3ds2gbxml missing.");
					Log($"Missing folder: {tools3ds2gbxml}");
					return;
				}

				var candidates = Directory.GetFiles(tools3ds2gbxml, "*.3ds", SearchOption.AllDirectories);
				if (candidates.Length == 0)
				{
					Status("No template .3ds found in tools/3ds2gbxml.");
					Log("No *.3ds found under: " + tools3ds2gbxml);
					return;
				}

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
			_viewport.Clear();
			UpdateOverlay();
			Status("Cleared.");
			_currentModel = null;
			ClearNormalsOverlay();
		}

		private void ToggleLogs_Click(object sender, RoutedEventArgs e)
		{
			SetLogsVisible(!_logsVisible);
		}

		private void Viewport_DragOver(object sender, DragEventArgs e)
		{
			e.Effects = DragDropEffects.None;

			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				var files = (string[])e.Data.GetData(DataFormats.FileDrop);
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
			_viewport.UpdateWireframe(true);
			Status("Wireframe ON");
		}

		private void Wireframe_Unchecked(object sender, RoutedEventArgs e)
		{
			_wireframeEnabled = false;
			_viewport.UpdateWireframe(false);
			Status("Wireframe OFF");
		}

		private void ColorCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
		{
			var selected = (e.AddedItems.Count > 0) ? e.AddedItems[0]?.ToString() : null;
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

			_viewport.ApplyOverrideColor(_overrideColor);
			Status($"Color: {_overrideColor}");
		}

		private void OpenModelFolder_Click(object sender, RoutedEventArgs e)
		{
			if (string.IsNullOrWhiteSpace(_currentModelPath) || !File.Exists(_currentModelPath))
			{
				Log("No model loaded.");
				return;
			}

			Process.Start(new ProcessStartInfo
			{
				FileName = "explorer.exe",
				Arguments = $"/select,\"{_currentModelPath}\"",
				UseShellExecute = true
			});
		}
		private static readonly string[] CanonicalMaterials =
		{
			"Concrete",
			"Pavement",
			"Grass",
			"Ice",
			"Metal",
			"Sand",
			"Dirt",
			"Turbo",
			"DirtRoad",
			"Rubber",
			"SlidingRubber",
			"Test",
			"Rock",
			"Water",
			"Wood",
			"Danger",
			"Asphalt",
			"WetDirtRoad",
			"WetAsphalt",
			"WetPavement",
			"WetGrass",
			"Snow",
			"ResonantMetal",
			"GolfBall",
			"GolfWall",
			"GolfGround",
			"Turbo2",
			"Bumper",
			"NotCollidable",
			"FreeWheeling",
			"TurboRoulette",
		};
		private static string? FindCanonicalMaterial(string name)
		{
			if (string.IsNullOrWhiteSpace(name)) return null;

			// super important: trim (souvent des espaces invisibles)
			name = name.Trim();

			return CanonicalMaterials.FirstOrDefault(m =>
				string.Equals(m, name, StringComparison.OrdinalIgnoreCase));
		}

		private void WarnMaterialCasing(IEnumerable<FaceMatGroup> faceGroups)
		{
			var warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var g in faceGroups)
			{
				var originalRaw = g.MaterialName;
				if (string.IsNullOrWhiteSpace(originalRaw))
					continue;

				var original = originalRaw.Trim();
				var canonical = FindCanonicalMaterial(original);

				// Si le matériau est connu (case-insensitive) mais pas la bonne casse exacte
				if (canonical != null && !original.Equals(canonical, StringComparison.Ordinal))
				{
					if (warned.Add(original))
					{
						LogWarn($"Material casing mismatch detected: '{originalRaw}' → expected '{canonical}'. " +
								"3ds2gbxml is case-sensitive; please rename the material in your 3D editor.");
					}
				}
			}
		}

		private static string ToTitleCaseInvariant(string s)
		{
			if (string.IsNullOrWhiteSpace(s)) return s;
			return char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
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

			// --------------------------------------------------
			// Prepare work directory
			// --------------------------------------------------
			var work3ds = _work.PrepareWorkDirForModel(_currentModelPath, out var projectDir);
			_currentWorkDir = projectDir;

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

			// --------------------------------------------------
			// Run 3ds2gbxml
			// --------------------------------------------------
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
				LogErr($"3ds2gbxml FAILED (exit {code})");
				Status($"XML failed (exit {code})");
				return;
			}

			// --------------------------------------------------
			// Collect XML outputs
			// --------------------------------------------------
			_work.CollectXmlOutputs(_currentWorkDir);

			var xmlFolder = Path.Combine(_currentWorkDir, "xml");
			var xmlFiles = DirectoryUtil.SafeListFiles(xmlFolder, "*.xml");

			LogOk($"3ds2gbxml OK (exit {code})");
			LogOk($"XML folder: {xmlFolder}");
			LogOk($"XML created: {xmlFiles.Count} file(s)");

			foreach (var f in xmlFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
				LogOk($"- {Path.GetFileName(f)}");

			// --------------------------------------------------
			// Prepare template + inject XML
			// --------------------------------------------------
			try
			{
				Status("Preparing template...");

				_currentTemplateDir = _template.PrepareTemplateForGbxc(_currentWorkDir);
				_work.InjectXmlIntoTemplate(_currentWorkDir, _currentTemplateDir);

				// Build trees (Root / ModelElements / Parts)
				var faceGroups = ThreeDsParser.ExtractFaceMaterialGroups4130(_currentModelPath);

				var res = TemplateTreeBuilder.BuildTrees(
					_currentTemplateDir,
					Path.GetFileNameWithoutExtension(_currentModelPath),
					faceGroups,
					s => Log(s),
					s => LogWarn(s)
				);

				LogOk($"Tree build OK: parts={res.CreatedParts.Count}, visuals={res.VisualXmls.Count}, surface={res.SurfaceXml ?? "null"}");

				foreach (var p in res.CreatedParts)
					LogOk($"- {p}");

				LogOk($"Template prepared: {_currentTemplateDir}");

				// Sanity check
				var solidXml = Path.Combine(_currentTemplateDir, "Template.Solid.xml");
				if (File.Exists(solidXml))
					LogOk("Template.Solid.xml OK");
				else
					LogWarn("Template.Solid.xml missing!");

				var copied = DirectoryUtil.SafeListFiles(_currentTemplateDir, "*.xml");
				LogOk($"Template XML now: {copied.Count} file(s)");

				foreach (var f in copied.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
					LogOk($"- {Path.GetFileName(f)}");

				Status($"XML OK + Template ready");
			}
			catch (Exception ex)
			{
				LogWarn("Template prep failed: " + ex.Message);
				Status("XML OK, template failed (see logs).");
			}
		}



		private void LogErrDedup(string line)
		{
			if (line == _lastErrLine)
			{
				_lastErrCount++;
				return;
			}

			// flush précédent
			if (_lastErrLine != null && _lastErrCount > 1)
				Log(Colors.Orange, $"(previous line repeated {_lastErrCount} times)");

			_lastErrLine = line;
			_lastErrCount = 1;
			Log("ERR: " + line);
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

				// Reuse already prepared template if available, otherwise prepare now
				var templateDir = _currentTemplateDir;

				if (string.IsNullOrWhiteSpace(templateDir) || !Directory.Exists(templateDir))
				{
					Status("Preparing template...");
					templateDir = _template.PrepareTemplateForGbxc(_currentWorkDir);
					_currentTemplateDir = templateDir;
					Log($"TemplateDir: {templateDir}");

					Status("Injecting XML...");
					_work.InjectXmlIntoTemplate(_currentWorkDir, templateDir);
				}
				else
				{
					LogOk($"Reusing template: {templateDir}");
				}

				var solidXml = Path.Combine(templateDir, "Template.Solid.xml");
				Log($"SolidXml: {solidXml}");
				if (!File.Exists(solidXml))
				{
					Status("Template.Solid.xml not found in work/template.");
					Log($"Missing: {solidXml}");
					return;
				}

				var buildDir = Path.Combine(_currentWorkDir, "build");
				Directory.CreateDirectory(buildDir);
				var outGbx = Path.Combine(buildDir, $"{modelName}.Solid.gbx");

				var args = $"\"{_paths.GbxcMain}\" -v -o \"{outGbx}\" \"{solidXml}\"";

				Log($"PY : {_paths.PythonExe}");
				Log($"CWD: {templateDir}");
				Log($"CMD: {_paths.PythonExe} {args}");
				Status("Compiling GBX...");

				int code = await _runner.RunAsync(
					_paths.PythonExe,
					args,
					s => Dispatcher.Invoke(() => Log(s)),
					s => Dispatcher.Invoke(() => LogErrDedup(s)),
					workingDirectory: templateDir
				);

				Log($"ExitCode: {code}");

				if (code != 0)
				{
					LogErr($"GBXC FAILED (exit {code})");
					Status($"GBX failed (exit {code})");
					return;
				}

				if (File.Exists(outGbx))
				{
					var fi = new FileInfo(outGbx);
					LogOk($"GBXC OK (exit {code})");
					LogOk($"GBX created: {fi.Name} ({fi.Length} bytes)");
					LogOk($"GBX path: {outGbx}");
					Status($"GBX OK: {outGbx}");
				}
				else
				{
					LogErr("GBXC reported success but output file is missing!");
					LogErr($"Expected: {outGbx}");
					Status("GBX missing (see logs).");
				}
			}
			catch (Exception ex)
			{
				Log("CompileGbx_Click CRASH: " + ex);
				Status("Compile crashed (see logs).");
				MessageBox.Show(ex.ToString(), "Compile crash", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		// ---------------- Model loading ----------------
		// ---------------- Model loading ----------------
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
				ClearLogs();
				Log($"Load: {path}");

				// ---- Parse 3DS materials ----
				var declaredMaterials = ThreeDsParser.ExtractMaterialNamesA000(path);    // 0xA000 (declared)
				var faceGroups = ThreeDsParser.ExtractFaceMaterialGroups4130(path);     // 0x4130 (used per faces)

				// Existing summary (keep)
				MaterialLogging.LogDetectedMaterialsWithFaces(
					s => Log(s),
					(c, s) => Log(c, s),
					declaredMaterials,
					faceGroups
				);

				// ---- NEW: warn about material name casing vs canonical TM surface names (at LOAD time) ----
				// This is what catches: "freewheeling" -> expected "FreeWheeling"
				{
					var warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

					var uniqueUsed = faceGroups
						.Select(g => g.MaterialName)
						.Where(n => !string.IsNullOrWhiteSpace(n))
						.Select(n => n!.Trim())
						.Distinct(StringComparer.OrdinalIgnoreCase)
						.ToList();

					foreach (var used in uniqueUsed)
					{
						if (!warned.Add(used))
							continue;

						var canonical = FindCanonicalMaterial(used); // uses your CanonicalMaterials list

						// Known material but wrong case
						if (canonical != null && !used.Equals(canonical, StringComparison.Ordinal))
						{
							LogWarn(
								$"Material casing mismatch detected: '{used}' → expected '{canonical}'. " +
								"3ds2gbxml is case-sensitive; rename the material in your 3D editor."
							);
						}
						// Unknown material name (not in canonical list) -> useful warning, but no spam
						else if (canonical == null)
						{
							LogWarn(
								$"Unknown material name: '{used}'. " +
								"3ds2gbxml may not recognize it and can fallback to a default (often Concrete). " +
								"Consider renaming it to a known surface name (e.g. Concrete, Dirt, Sand, FreeWheeling...)."
							);
						}
					}
				}

				// ---- Import Helix ----
				var model = _importer.Load(path);
				_currentModelPath = path;
				_currentModel = model;

				// Apply materials by 0x4130 mapping
				var geoms = new List<GeometryModel3D>();
				ViewportService.CollectGeometryModels(model, geoms);
				ViewportService.ApplyFaceGroupColors(geoms, faceGroups, _overrideColor, (c, s) => Log(c, s));

				// Display
				_viewport.ShowModel(model);
				UpdateOverlay();

				_viewport.UpdateWireframe(_wireframeEnabled);
				if (_faceOrientationEnabled)
					ApplyFaceOrientationMode();

				UpdateNormalsOverlay();
				Status($"Loaded: {Path.GetFileName(path)} ({path})");
			}
			catch (Exception ex)
			{
				Status("Load failed (see logs).");
				Log("ERROR: " + ex);
				MessageBox.Show(ex.ToString(), "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			

		}

	}
}
