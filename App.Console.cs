﻿
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace MarvinsAIRA
{
	public partial class App : Application
	{
		private ReaderWriterLock _console_readerWriterLock = new();
		private FileStream? _console_fileStream = null;

		public void InitializeConsole()
		{
			WriteLine( "InitializeConsole called.", true );

			var filePath = Path.Combine( DocumentsFolder, "Console.log" );

			if ( File.Exists( filePath ) )
			{
				var lastWriteTime = File.GetLastWriteTime( filePath );

				if ( lastWriteTime.CompareTo( DateTime.Now.AddMinutes( -15 ) ) < 0 )
				{
					File.Delete( filePath );
				}
			}

			_console_fileStream = new FileStream( filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite );
		}

		public void StopConsole()
		{
			WriteLine( "StopConsole called.", true );

			_console_fileStream?.Close();
			_console_fileStream?.Dispose();

			_console_fileStream = null;
		}

		public void WriteLine( string message, bool addBlankLine = false )
		{
			var blankLine = addBlankLine ? "\r\n" : string.Empty;

			var messageWithTime = $"{blankLine}{DateTime.Now}   {message}";

			Debug.WriteLine( message );

			if ( _console_fileStream != null )
			{
				try
				{
					_console_readerWriterLock.AcquireWriterLock( 250 );

					try
					{
						var bytes = new UTF8Encoding( true ).GetBytes( $"{messageWithTime}\r\n" );

						_console_fileStream.Write( bytes, 0, bytes.Length );
						_console_fileStream.Flush();
					}
					finally
					{
						_console_readerWriterLock.ReleaseWriterLock();
					}
				}
				catch ( ApplicationException )
				{
				}

			}

			Dispatcher.BeginInvoke( () =>
			{
				var mainWindow = MarvinsAIRA.MainWindow.Instance;

				if ( mainWindow != null )
				{
					mainWindow.Console_TextBox.Text += $"{messageWithTime}\r\n";
					mainWindow.Console_TextBox.CaretIndex = mainWindow.Console_TextBox.Text.Length;
					mainWindow.Console_TextBox.ScrollToEnd();
				}
			} );
		}
	}
}
