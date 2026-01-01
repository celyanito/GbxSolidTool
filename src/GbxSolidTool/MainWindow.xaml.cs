using HelixToolkit.Wpf;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Collections.Generic;


namespace GbxSolidTool
{
	public partial class MainWindow : Window
	{
		private LinesVisual3D? _wireframeLines;

		private MeshGeometry3D? _mesh;

		private readonly AppPaths _paths = new();

		private ProjectPaths? _proj;

		private bool _templatePrepared;

		private string? _current3dsPath;

		private DefaultLights? _lights;

		// Current loaded model
		private ModelVisual3D? _modelVisual;

		private void LoadModelIntoViewport(string path)
		{
			if (View3D == null)
				return;

			try
			{
				// Ajouter les lights une seule fois (sinon écran noir)
				if (_lights == null)
				{
					_lights = new DefaultLights();
					View3D.Children.Add(_lights);
				}

				// Retirer l'ancien modèle
				if (_modelVisual != null)
				{
					View3D.Children.Remove(_modelVisual);
					_modelVisual = null;
				}

				var importer = new ModelImporter();
				Model3D model = importer.Load(path);

				_mesh = ExtractFirstMesh(model);

				_modelVisual = new ModelVisual3D { Content = model };
				View3D.Children.Add(_modelVisual);

				ApplyWireframeMaterial();

				// Auto-fit caméra
				if (ChkAutoFit?.IsChecked == true)
					View3D.ZoomExtents();

				AppendLog("OK: Model displayed in viewport.");
			}
			catch (Exception ex)
			{
				AppendLog("ERROR: Could not display model:");
				AppendLog(ex.ToString());
			}
		}

		private static void CopyDirectory(string sourceDir, string destDir)
		{
			Directory.CreateDirectory(destDir);

			foreach (var file in Directory.GetFiles(sourceDir))
			{
				var name = Path.GetFileName(file);
				File.Copy(file, Path.Combine(destDir, name), overwrite: true);
			}

			foreach (var dir in Directory.GetDirectories(sourceDir))
			{
				var name = Path.GetFileName(dir);
				CopyDirectory(dir, Path.Combine(destDir, name));
			}
		}

		public MainWindow()
		{
			InitializeComponent();
			Loaded += (_, __) => UpdateDropHint();


			AppendLog("Ready.");
			AppendLog($"Repo3ds2gbxml: {_paths.Repo3ds2Gbxml}");
			AppendLog($"XmlRoot:       {_paths.XmlRoot}");
			AppendLog($"PythonExe:     {_paths.PythonExe}");


			UpdateUiState();
		}

		private void UpdateDropHint()
		{
			if (OverlayHint == null) return;

			// Cache l'overlay dès qu'on a un .3ds chargé
			OverlayHint.Visibility = string.IsNullOrWhiteSpace(_current3dsPath)
				? Visibility.Visible
				: Visibility.Collapsed;
		}

		// ---------- UI Actions ----------
		
		private void BtnOpenBuild_Click(object sender, RoutedEventArgs e)
		{
			if (_proj == null) return;

			if (!Directory.Exists(_proj.BuildDir))
				Directory.CreateDirectory(_proj.BuildDir);

			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
			{
				FileName = _proj.BuildDir,
				UseShellExecute = true
			});
		}

		private async void BtnBuild_Click(object sender, RoutedEventArgs e)
		{
			if (_proj == null)
			{
				AppendLog("ERROR: No project loaded.");
				return;
			}

			string gbxcMain = @"C:\TMTools\gbxc\main.py";
			if (!File.Exists(gbxcMain))
			{
				AppendLog("ERROR: gbxc main.py not found:");
				AppendLog(gbxcMain);
				return;
			}

			// Input XML = Template.Solid.xml (dans template/)
			string solidXml = Path.Combine(_proj.TemplateDir, "Template.Solid.xml");
			if (!File.Exists(solidXml))
			{
				AppendLog("ERROR: Template.Solid.xml not found:");
				AppendLog(solidXml);
				AppendLog("Hint: run Prepare Template first.");
				return;
			}

			Directory.CreateDirectory(_proj.BuildDir);

			// Output GBX
			string outGbx = Path.Combine(_proj.BuildDir, $"{_proj.BaseName}.Solid.gbx");

			AppendLog("");
			AppendLog("=== COMPILE SOLID (GBXC) ===");
			AppendLog($"XML: {solidXml}");
			AppendLog($"OUT: {outGbx}");

			// Python = ton venv
			string pythonExe = _paths.PythonExe;
			if (!File.Exists(pythonExe))
			{
				AppendLog("ERROR: Python exe not found:");
				AppendLog(pythonExe);
				return;
			}

			// Args gbxc
			string args = $"\"{gbxcMain}\" -v -o \"{outGbx}\" \"{solidXml}\"";
			AppendLog($"CMD: {pythonExe} {args}");

			BtnBuild.IsEnabled = false;

			var rootPath = Path.Combine(_proj.TemplateDir, "Root.CPlugTree.xml");
			AppendLog($"DBG: workingDir = {_proj.TemplateDir}");
			AppendLog($"DBG: Root exists = {File.Exists(rootPath)}");

			if (File.Exists(rootPath))
			{
				var bytes = File.ReadAllBytes(rootPath);
				AppendLog($"DBG: Root size = {bytes.Length}");
				var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(32, bytes.Length));
				AppendLog($"DBG: Root head = {head.Replace("\r", "\\r").Replace("\n", "\\n")}");
			}


			try
			{
				int exitCode = await ProcessRunner.RunAsync(
					exePath: pythonExe,
					arguments: args,
					workingDir: _proj.TemplateDir, // important : paths relatifs cohérents
					onLine: AppendLog
				);

				AppendLog($"ExitCode: {exitCode}");

				if (exitCode != 0)
				{
					AppendLog("ERROR: gbxc failed.");
					return;
				}

				if (!File.Exists(outGbx))
				{
					AppendLog("ERROR: Output .gbx not created.");
					return;
				}

				var size = new FileInfo(outGbx).Length;
				AppendLog($"OK: Solid compiled ({size} bytes).");
				AppendLog(outGbx);
			}
			catch (Exception ex)
			{
				AppendLog("ERROR during gbxc:");
				AppendLog(ex.ToString());
			}
			finally
			{
				BtnBuild.IsEnabled = true;
			}
		}



		private void BtnOpen3ds_Click(object sender, RoutedEventArgs e)
		{
			var dlg = new OpenFileDialog
			{
				Filter = "3D Studio (*.3ds)|*.3ds",
				Title = "Open .3ds model"
			};

			if (dlg.ShowDialog() == true)
				Load3ds(dlg.FileName);
		}

		private void Preview_DragOver(object sender, DragEventArgs e)
		{
			e.Effects = IsSingle3dsDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		}

		private void Preview_Drop(object sender, DragEventArgs e)
		{
			if (!IsSingle3dsDrop(e)) return;

			var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
			Load3ds(files[0]);
		}

		private static bool IsSingle3dsDrop(DragEventArgs e)
		{
			if (!e.Data.GetDataPresent(DataFormats.FileDrop))
				return false;

			if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1)
				return false;

			if (files is not { Length: 1 }) return false;

			return string.Equals(Path.GetExtension(files[0]), ".3ds", StringComparison.OrdinalIgnoreCase);
		}

		private void ChkWireframe_Changed(object sender, RoutedEventArgs e)
		{
			ApplyWireframeMaterial();
		}

		private async void BtnConvert_Click(object sender, RoutedEventArgs e)
		{
			if (_proj == null)
			{
				AppendLog("ERROR: No project loaded.");
				return;
			}

			if (string.IsNullOrWhiteSpace(_proj.Input3dsPath) || !File.Exists(_proj.Input3dsPath))
			{
				AppendLog("ERROR: Input .3ds file not found.");
				return;
			}

			AppendLog("");
			AppendLog("=== CONVERT 3DS → XML ===");
			AppendLog($"Input: {_proj.Input3dsPath}");
			AppendLog($"Out:   {_proj.GeneratedDir}");

			try
			{
				// 1) Nettoyer le dossier generated (sécurité)
				if (Directory.Exists(_proj.GeneratedDir))
					Directory.Delete(_proj.GeneratedDir, recursive: true);

				Directory.CreateDirectory(_proj.GeneratedDir);

				// 2) Construire la commande 3ds2gbxml
				var script = Path.Combine(_paths.Repo3ds2Gbxml, "3ds2gbxml.py");
				if (!File.Exists(script))
				{
					AppendLog($"ERROR: 3ds2gbxml.py not found: {script}");
					return;
				}

				// Flags validés : -v -s
				var args = $"\"{script}\" -v -s \"{_proj.Input3dsPath}\"";
				AppendLog($"CMD: {_paths.PythonExe} {args}");

				BtnConvert.IsEnabled = false;

				// 3) Lancer la conversion
				int exitCode = await ProcessRunner.RunAsync(
					exePath: _paths.PythonExe,
					arguments: args,
					workingDir: _proj.GeneratedDir,
					onLine: AppendLog
				);

				AppendLog($"ExitCode: {exitCode}");

				if (exitCode != 0)
				{
					AppendLog("ERROR: 3ds2gbxml failed.");
					return;
				}

				// 4) Vérification des fichiers générés
				var xmlFiles = Directory.GetFiles(_proj.GeneratedDir, "*.xml", SearchOption.AllDirectories);

				AppendLog($"XML generated: {xmlFiles.Length}");
				foreach (var f in xmlFiles)
					AppendLog(" - " + Path.GetFileName(f));

				if (!xmlFiles.Any(f => f.Contains("CPlugSurface", StringComparison.OrdinalIgnoreCase)))
				{
					AppendLog("ERROR: No CPlugSurface XML found.");
					return;
				}

				if (!xmlFiles.Any(f => f.Contains("CPlugVisual", StringComparison.OrdinalIgnoreCase)))
				{
					AppendLog("ERROR: No CPlugVisual XML found.");
					return;
				}

				AppendLog("OK: Conversion finished.");

				// 5) Activer l'étape suivante
				if (BtnPrepareTemplate != null)
					BtnPrepareTemplate.IsEnabled = true;
			}
			catch (Exception ex)
			{
				AppendLog("ERROR during conversion:");
				AppendLog(ex.ToString());
			}
			finally
			{
				BtnConvert.IsEnabled = true;
			}
		}

		private void BtnOpenXml_Click(object sender, RoutedEventArgs e)
		{
			if (_proj == null)
			{
				AppendLog("ERROR: No project loaded.");
				return;
			}

			var dir = _proj.GeneratedDir; // ou _proj.TemplateDir si tu préfères

			if (!Directory.Exists(dir))
			{
				AppendLog($"ERROR: Folder does not exist: {dir}");
				return;
			}

			try
			{
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
				{
					FileName = dir,
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				AppendLog("ERROR: Can't open folder:");
				AppendLog(ex.Message);
			}
		}


		// ---------- 3DS Loading + View ----------

		private void Load3ds(string path)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
				{
					AppendLog("ERROR: Invalid .3ds path.");
					return;
				}

				AppendLog("");
				AppendLog("=== LOAD 3DS ===");
				AppendLog($"Source: {path}");

				// 1) Créer un nouveau projet (Scene_xxx_no_001, dossiers, copie locale)
				_proj = ProjectManager.CreateNewProject(path);

				AppendLog($"ProjectDir: {_proj.ProjectDir}");
				AppendLog($"Input3ds:   {_proj.Input3dsPath}");
				AppendLog($"Generated:  {_proj.GeneratedDir}");
				AppendLog($"Template:   {_proj.TemplateDir}");
				AppendLog($"Build:      {_proj.BuildDir}");

				// 2) Toujours travailler sur la copie locale du .3ds
				_current3dsPath = _proj.Input3dsPath;
				TxtFile.Text = _current3dsPath;
				_templatePrepared = false;
				UpdateUiState();



				// 3) (Optionnel) reset UI / états précédents
				BtnConvert.IsEnabled = true;
				BtnPrepareTemplate.IsEnabled = false;
				BtnBuild.IsEnabled = false;

				// 4) Charger le modèle dans le viewport 3D
				LoadModelIntoViewport(_current3dsPath);
				UpdateDropHint();
				UpdateUiState();


				AppendLog("OK: Model loaded.");
			}
			catch (Exception ex)
			{
				AppendLog("ERROR while loading .3ds:");
				AppendLog(ex.Message);
			}
		}


		private void BtnPrepareTemplate_Click(object sender, RoutedEventArgs e)
		{
			if (_proj == null) return;

			AppendLog("");
			AppendLog("=== PREPARE TEMPLATE ===");

			ProjectManager.CopyTemplateInto(_proj);
			AppendLog("Template copied.");

			ProjectManager.CopyGeneratedXmlIntoTemplate(_proj);
			AppendLog("Generated XML copied into template.");

			AppendLog("OK: Template prepared (NO PATCH).");
			_templatePrepared = true;
			UpdateUiState();
		}


		private void InjectModelIntoViewport(Model3DGroup group)
		{
			// Remove old visual
			if (_modelVisual != null)
				View3D.Children.Remove(_modelVisual);

			_modelVisual = new ModelVisual3D { Content = group };
			View3D.Children.Add(_modelVisual);
		}

		private void ApplyWireframeMaterial()
		{
			if (View3D == null || ChkWireframe == null)
				return;

			// remove existing wireframe
			if (_wireframeLines != null)
			{
				View3D.Children.Remove(_wireframeLines);
				_wireframeLines = null;
			}

			if (ChkWireframe.IsChecked != true)
				return;

			if (_mesh == null)
			{
				AppendLog("WARN: No mesh found for wireframe.");
				return;
			}
	
			var points = BuildWireframeLines(_mesh);
			if (points.Count == 0)
			{
				AppendLog("WARN: Wireframe lines empty.");
				return;
			}

			_wireframeLines = new LinesVisual3D
			{
				Color = Colors.White,
				Thickness = 1.0,
				Points = new Point3DCollection(points)
			};

			View3D.Children.Add(_wireframeLines);
		}

		private static MeshGeometry3D? ExtractFirstMesh(Model3D model)
		{
			if (model is GeometryModel3D gm && gm.Geometry is MeshGeometry3D mg)
				return mg;

			if (model is Model3DGroup g)
			{
				foreach (var c in g.Children)
				{
					var found = ExtractFirstMesh(c);
					if (found != null) return found;
				}
			}
			return null;
		}
		private static List<Point3D> BuildWireframeLines(MeshGeometry3D mesh)
		{
			var pts = new List<Point3D>();
			if (mesh.Positions == null || mesh.TriangleIndices == null || mesh.TriangleIndices.Count < 3)
				return pts;

			// store edges as (min,max) to deduplicate
			var edges = new HashSet<(int a, int b)>();

			void AddEdge(int i0, int i1)
			{
				if (i0 == i1) return;
				var a = i0 < i1 ? i0 : i1;
				var b = i0 < i1 ? i1 : i0;
				edges.Add((a, b));
			}

			var ti = mesh.TriangleIndices;
			for (int i = 0; i + 2 < ti.Count; i += 3)
			{
				int a = ti[i];
				int b = ti[i + 1];
				int c = ti[i + 2];

				AddEdge(a, b);
				AddEdge(b, c);
				AddEdge(c, a);
			}

			// convert edges to line segments
			foreach (var (a, b) in edges)
			{
				if (a < 0 || b < 0 || a >= mesh.Positions.Count || b >= mesh.Positions.Count)
					continue;

				pts.Add(mesh.Positions[a]);
				pts.Add(mesh.Positions[b]);
			}

			return pts;
		}



		private static string BuildStats(Model3DGroup group)
		{
			int geomCount = 0;
			int triApprox = 0;

			void Walk(Model3D m)
			{
				if (m is GeometryModel3D gm && gm.Geometry is MeshGeometry3D mg)
				{
					geomCount++;
					if (mg.TriangleIndices != null)
						triApprox += mg.TriangleIndices.Count / 3;
				}
				else if (m is Model3DGroup g)
				{
					foreach (var c in g.Children) Walk(c);
				}
			}

			Walk(group);

			var b = group.Bounds; // Rect3D
			return $"Meshes: {geomCount}\nTriangles (approx): {triApprox}\nBounds: X={b.SizeX:0.###} Y={b.SizeY:0.###} Z={b.SizeZ:0.###}";
		}


		private void UpdateUiState()
		{
			if (!IsLoaded) return;

			bool hasProj = _proj != null;
			bool has3ds = !string.IsNullOrWhiteSpace(_current3dsPath);

			if (BtnConvert != null)
				BtnConvert.IsEnabled = hasProj && has3ds;

			if (BtnPrepareTemplate != null)
				BtnPrepareTemplate.IsEnabled = hasProj; // ou plus strict selon toi

			if (BtnBuild != null)
				BtnBuild.IsEnabled = _templatePrepared;

			if (TxtFile != null)
				TxtFile.Text = _current3dsPath ?? "";
		}




		// ---------- Log ----------

		private void AppendLog(string line)
		{
			if (!Dispatcher.CheckAccess())
			{
				Dispatcher.Invoke(() => AppendLog(line));
				return;
			}

			var sb = new StringBuilder(LogBox.Text);
			if (sb.Length > 0) sb.AppendLine();
			sb.Append(line);
			LogBox.Text = sb.ToString();
			LogBox.ScrollToEnd();
		}

    }
}
