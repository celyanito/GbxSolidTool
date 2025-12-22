using System;
using System.IO;
using System.Windows;

namespace GbxSolidTool
{
	public partial class App : Application
	{
		protected override void OnStartup(StartupEventArgs e)
		{
			// Log exceptions (même si la fenêtre ne s'affiche pas)
			AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
				File.AppendAllText(@"C:\TMTools\GbxSolidTool_crash.txt", ex.ExceptionObject + Environment.NewLine);

			DispatcherUnhandledException += (_, ex) =>
			{
				File.AppendAllText(@"C:\TMTools\GbxSolidTool_crash.txt", ex.Exception + Environment.NewLine);
				ex.Handled = true; // évite fermeture brutale
			};

			try
			{
				base.OnStartup(e);
			}
			catch (Exception ex2)
			{
				File.AppendAllText(@"C:\TMTools\GbxSolidTool_crash.txt", ex2 + Environment.NewLine);
				Shutdown(1);
			}
		}
	}
}
