﻿
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace MarvinsAIRA
{
	public partial class MainWindow : Window
	{
		#region Properties

		private bool _win_initialized = false;
		private bool _win_updateLoopRunning = false;
		private bool _win_pauseButtons = false;

		private nint _win_windowHandle = 0;

		private readonly System.Timers.Timer _win_timer = new( 100 );

		private int _win_keepThreadsAlive = 1;
		private int _win_sendForceFeedbackTestSignalCounter = 0;

		private float _win_guiUpdateTimer = 0;

		private readonly Stopwatch _win_stopwatch = new();

		public readonly AutoResetEvent _win_autoResetEvent = new( false );

		public static MainWindow? Instance { get; private set; } = null;

		#endregion

		#region Window

		public MainWindow()
		{
			InitializeComponent();

			Instance = this;
		}

		private void Window_Closing( object sender, System.ComponentModel.CancelEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "MainWindow.Window_Closing called." );

			app.WriteLine( "...stopping the main window timer..." );

			_win_timer.Stop();
			_win_timer.Dispose();

			app.WriteLine( "...main window timer stopped." );

			if ( _win_updateLoopRunning )
			{
				app.WriteLine( "...killing the update loop..." );

				_win_keepThreadsAlive = 0;

				_win_autoResetEvent.Set();

				while ( _win_updateLoopRunning )
				{
					Thread.Sleep( 0 );
				}

				app.WriteLine( "...update loop killed..." );
			}

			app.Stop();

			Instance = null;
		}

		private void Window_Activated( object sender, EventArgs e )
		{
			if ( !_win_initialized )
			{
				var app = (App) Application.Current;

				_win_windowHandle = new WindowInteropHelper( this ).Handle;

				app.Initialize( _win_windowHandle );

				app.WriteLine( "" );
				app.WriteLine( "Starting the update loop..." );

				_win_stopwatch.Restart();

				var thread = new Thread( UpdateLoop );

				thread.Start();

				while ( !_win_updateLoopRunning )
				{
					Thread.Sleep( 0 );
				}

				app.WriteLine( "...update loop started." );

				app.WriteLine( "" );
				app.WriteLine( "Starting the window timer..." );

				_win_timer.Elapsed += OnTimer;
				_win_timer.Start();

				app.WriteLine( "...window timer started." );

				LoadRecording();

				app.WriteLine( "" );
				app.WriteLine( $"{Title} has been initialized!" );

				if ( app.Settings.TopmostWindow )
				{
					app.WriteLine( "" );
					app.WriteLine( "Setting window to be topmost." );

					Topmost = true;
				}

				if ( app.Settings.StartMinimized )
				{
					app.WriteLine( "" );
					app.WriteLine( "Minimizing the window." );

					WindowState = WindowState.Minimized;
				}

				_win_initialized = true;
			}
		}

		#endregion

		#region Update loop

		private void OnTimer( object? sender, EventArgs e )
		{
			var app = (App) Application.Current;

			if ( !app._irsdk_connected )
			{
				_win_autoResetEvent.Set();
			}
		}

		private void UpdateLoop()
		{
			var app = (App) Application.Current;

			_win_updateLoopRunning = true;

			try
			{
				while ( _win_keepThreadsAlive == 1 )
				{
					_win_autoResetEvent?.WaitOne();

					if ( _win_keepThreadsAlive == 1 )
					{
						var deltaTime = Math.Min( 0.1f, (float) _win_stopwatch.Elapsed.TotalSeconds );

						if ( deltaTime >= 1f / 120f )
						{
							_win_stopwatch.Restart();

							app.UpdateSettings( deltaTime );

							if ( !_win_pauseButtons )
							{
								app.UpdateInputs( deltaTime );
							}

							app.UpdateForceFeedback( deltaTime, !_win_pauseButtons, _win_windowHandle );
							app.UpdateWindSimulator();

							if ( _win_sendForceFeedbackTestSignalCounter > 0 )
							{
								if ( _win_sendForceFeedbackTestSignalCounter == 1 )
								{
									app.UpdateConstantForce( [ 0 ] );
								}
								else
								{
									app.SendTestForceFeedbackSignal( ( _win_sendForceFeedbackTestSignalCounter & 1 ) == 0 );
								}

								_win_sendForceFeedbackTestSignalCounter--;
							}

							_win_guiUpdateTimer -= deltaTime;

							if ( _win_guiUpdateTimer <= 0f )
							{
								_win_guiUpdateTimer = 0.05f;

								Dispatcher.BeginInvoke( () =>
								{
									// Force feedback status

									if ( !app.FFB_Initialized )
									{
										ForceFeedback_StatusBarItem.Content = "FFB: Fault";
										ForceFeedback_StatusBarItem.Foreground = Brushes.Red;
									}
									else if ( app.FFB_ClippedTimer > 0 )
									{
										ForceFeedback_StatusBarItem.Content = "FFB: CLIPPING!";
										ForceFeedback_StatusBarItem.Foreground = Brushes.Red;
									}
									else if ( app.Settings.ForceFeedbackEnabled )
									{
										ForceFeedback_StatusBarItem.Content = $"FFB: {( app.FFB_LastMagnitudeSentToWheel * 100f / App.DI_FFNOMINALMAX ):F0}%";
										ForceFeedback_StatusBarItem.Foreground = Brushes.ForestGreen;
									}
									else
									{
										ForceFeedback_StatusBarItem.Content = $"FFB: Off";
										ForceFeedback_StatusBarItem.Foreground = Brushes.Gray;
									}

									// Pretty graph

									if ( app._ffb_drawPrettyGraph )
									{
										app._ffb_writeableBitmap?.WritePixels( new Int32Rect( 0, 0, App.FFB_WRITEABLE_BITMAP_WIDTH, App.FFB_WRITEABLE_BITMAP_HEIGHT ), app._ffb_pixels, App.FFB_PIXELS_BUFFER_STRIDE, 0, 0 );
									}

									// Recording status

									if ( app._ffb_recordingNow )
									{
										var recordTime = GetRecordingIndexAsTime();

										Recording_Label.Content = $"Recording - {recordTime}";
										Recording_Label.Visibility = ( ( app._irsdk_tickCount % 60 ) < 15 ) ? Visibility.Hidden : Visibility.Visible;
									}
									else
									{
										Recording_Label.Visibility = Visibility.Hidden;
									}

									// Playback status

									if ( app._ffb_playingBackNow )
									{
										var recordTime = GetRecordingIndexAsTime();

										Playback_Label.Content = $"Playback - {recordTime}";
										Playback_Label.Visibility = Visibility.Visible;
									}
									else
									{
										Playback_Label.Visibility = Visibility.Hidden;
									}

									// Steering wheel angle

									var steeringWheelAngleInDegrees = app._irsdk_steeringWheelAngle * 180f / Math.PI;

									SteeringWheel_Image.RenderTransform = new RotateTransform( -steeringWheelAngleInDegrees );

									SteeringWheel_Label.Content = $"{steeringWheelAngleInDegrees:F0}°";

									if ( (string) SteeringWheel_Label.Content == "-0°" )
									{
										SteeringWheel_Label.Content = "0°";
									}

									// Speed

									if ( app._irsdk_displayUnits == 0 )
									{
										Speed_Label.Content = $"{app._irsdk_speed * App.MPS_TO_MPH:F0} MPH";
									}
									else
									{
										Speed_Label.Content = $"{app._irsdk_speed * App.MPS_TO_KPH:F0} KPH";
									}

									// Yaw rate

									var yawRateInDegreesPerSecond = app._irsdk_yawRate * 180f / Math.PI;

									YawRate_Label.Content = $"{yawRateInDegreesPerSecond:F0}°/sec";

									if ( (string) YawRate_Label.Content == "-0°/sec" )
									{
										YawRate_Label.Content = $"0°/sec";
									}

									// Lateral force

									LateralForce_Label.Content = $"{app._irsdk_latAccel:F0} m⋅s²";

									if ( (string) LateralForce_Label.Content == "-0 m⋅s²" )
									{
										LateralForce_Label.Content = $"0 m⋅s²";
									}

									// Yaw rate factor (instant)

									YawRateFactorInstant_Label.Content = $"{app.FFB_YawRateFactorInstant:F2}";

									// Yaw rate factor (average)

									YawRateFactorAverage_Label.Content = $"{app.FFB_YawRateFactorAverage:F2}";

									// Wind status

									if ( !app.Wind_Initialized )
									{
										Wind_StatusBarItem.Content = "Wind: Fault";
										Wind_StatusBarItem.Foreground = Brushes.Red;
									}
									else if ( app.Settings.WindSimulatorEnabled )
									{
										Wind_StatusBarItem.Content = $"Wind: {app.Wind_CurrentMagnitude:F0}%";
										Wind_StatusBarItem.Foreground = Brushes.ForestGreen;
									}
									else
									{
										Wind_StatusBarItem.Content = $"Wind: Off";
										Wind_StatusBarItem.Foreground = Brushes.Gray;
									}
								} );
							}
						}
					}
				}
			}
			catch ( Exception exception )
			{
				app.WriteLine( "" );
				app.WriteLine( $"Exception caught inside the update loop: {exception.Message.Trim()}" );
			}

			_win_updateLoopRunning = false;
		}

		private string GetRecordingIndexAsTime()
		{
			var app = (App) Application.Current;

			var minutes = app._ffb_recordedSteeringWheelTorqueBufferIndex / ( 360 * 60 );
			var seconds = app._ffb_recordedSteeringWheelTorqueBufferIndex % ( 360 * 60 ) / 360f;

			return $"{minutes}:{seconds:00.0}";
		}

		#endregion

		#region Generic text box <---> slider functions

		[GeneratedRegex( "[^0123456789.]" )]
		private partial Regex NotDecimalNumbersRegex();

		[GeneratedRegex( "[0123456789.]" )]
		private partial Regex DecimalNumbersRegex();

		private void TextBox_GotKeyboardFocus( object sender, KeyboardFocusChangedEventArgs e )
		{
			if ( sender is TextBox textBox )
			{
				textBox.Text = NotDecimalNumbersRegex().Replace( textBox.Text, "" );
			}
		}

		private void TextBox_PreviewTextInput( object sender, TextCompositionEventArgs e )
		{
			if ( sender is TextBox textBox )
			{
				if ( !DecimalNumbersRegex().IsMatch( e.Text ) || ( e.Text.Contains( '.' ) && textBox.Text.Contains( '.' ) ) )
				{
					e.Handled = true;
				}
			}
		}

		private void TextBox_LostKeyboardFocus( object sender, KeyboardFocusChangedEventArgs e )
		{
			if ( sender is TextBox textBox )
			{
				if ( !float.TryParse( textBox.Text, out var value ) )
				{
					value = 0;
				}

				var sliderObject = GetNextTab( textBox, textBox.Parent, true );

				if ( sliderObject is Slider slider )
				{
					slider.Value = value;
				}
			}
		}

		public static DependencyObject? GetNextTab( DependencyObject element, DependencyObject containerElement, bool goDownOnly )
		{
			var keyboardNavigation = typeof( FrameworkElement )?.GetProperty( "KeyboardNavigation", BindingFlags.NonPublic | BindingFlags.Static )?.GetValue( null );

			var method = keyboardNavigation?.GetType()?.GetMethod( "GetNextTab", BindingFlags.NonPublic | BindingFlags.Instance );

			if ( method != null )
			{
				return method.Invoke( keyboardNavigation, [ element, containerElement, goDownOnly ] ) as DependencyObject;
			}

			return null;
		}

		#endregion

		#region Force feedback tab

		private void ForceFeedback_CheckBox_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "ForceFeedback_CheckBox_Click called." );

			var checkBox = (CheckBox) sender;

			if ( checkBox.IsChecked == true )
			{
				app.InitializeForceFeedback( _win_windowHandle );
			}
			else
			{
				app.StopForceFeedback();
			}
		}

		private void FFBDevice_ComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
		{
			if ( _win_initialized )
			{
				var app = (App) Application.Current;

				app.WriteLine( "" );
				app.WriteLine( "FFBDevice_ComboBox_SelectionChanged called." );

				app.InitializeForceFeedback( _win_windowHandle );
			}
		}

		private void ForceFeedbackTest_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "ForceFeedbackTest_Button_Click called." );

			_win_sendForceFeedbackTestSignalCounter = 11;
		}

		private void Record_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "Record_Button_Click called." );

			if ( !app._irsdk_connected )
			{
				app.WriteLine( "...the iRacing simulator is not running, so ignoring this." );
			}
			else
			{
				app._ffb_recordedSteeringWheelTorqueBufferIndex = 0;

				var wasRecording = app._ffb_recordingNow;

				app._ffb_playingBackNow = false;
				app._ffb_recordingNow = !app._ffb_recordingNow;

				if ( wasRecording )
				{
					SaveRecording();
				}
				else
				{
					Array.Clear( app._ffb_recordedSteeringWheelTorqueBuffer );
				}

				if ( app._ffb_recordingNow && !app._ffb_drawPrettyGraph )
				{
					TogglePrettyGraph();
				}

				app.WriteLine( $"...recording is now {app._ffb_recordingNow}" );
				app.WriteLine( $"...playback is now {app._ffb_playingBackNow}" );
			}
		}

		private void Play_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "PlayButton_Click called." );

			if ( !app._irsdk_connected )
			{
				app.WriteLine( "...the iRacing simulator is not running, so ignoring this." );
			}
			else
			{
				var wasRecording = app._ffb_recordingNow;

				app._ffb_playingBackNow = !app._ffb_playingBackNow;
				app._ffb_recordingNow = false;

				if ( wasRecording )
				{
					SaveRecording();
				}

				app._ffb_recordedSteeringWheelTorqueBufferIndex = 0;

				if ( app._ffb_playingBackNow && !app._ffb_drawPrettyGraph )
				{
					TogglePrettyGraph();
				}

				app.WriteLine( $"...playback is now {app._ffb_playingBackNow}" );
				app.WriteLine( $"...recording is now {app._ffb_recordingNow}" );
			}
		}

		private void ResetForceFeedback_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "ResetForceFeedback_Button_Click called." );

			app.Settings.ReinitForceFeedbackButtons = ShowMapButtonsWindow( app.Settings.ReinitForceFeedbackButtons );
		}

		private void AutoOverallScale_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "AutoOverallScale_Button_Click called." );

			app.Settings.AutoOverallScaleButtons = ShowMapButtonsWindow( app.Settings.AutoOverallScaleButtons );
		}

		private void DecreaseOverallScale_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "DecreaseOverallScale_Button_Click called." );

			app.Settings.DecreaseOverallScaleButtons = ShowMapButtonsWindow( app.Settings.DecreaseOverallScaleButtons );
		}

		private void IncreaseOverallScale_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "IncreaseOverallScale_Button_Click called." );

			app.Settings.IncreaseOverallScaleButtons = ShowMapButtonsWindow( app.Settings.IncreaseOverallScaleButtons );
		}

		private void DecreaseDetailScale_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "DecreaseDetailScale_Button_Click called." );

			app.Settings.DecreaseDetailScaleButtons = ShowMapButtonsWindow( app.Settings.DecreaseDetailScaleButtons );
		}

		private void IncreaseDetailScale_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "IncreaseDetailScale_Button_Click called." );

			app.Settings.IncreaseDetailScaleButtons = ShowMapButtonsWindow( app.Settings.IncreaseDetailScaleButtons );
		}

		private void Frequency_Slider_ValueChanged( object sender, RoutedPropertyChangedEventArgs<double> e )
		{
			if ( _win_initialized )
			{
				var app = (App) Application.Current;

				app.WriteLine( "" );
				app.WriteLine( "Frequency_Slider_ValueChanged called." );

				app.ScheduleReinitializeForceFeedback();
			}
		}

		private void TogglePrettyGraph_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "TogglePrettyGraph_Button_Click called." );

			TogglePrettyGraph();
		}

		private static void LoadRecording()
		{
			var app = (App) Application.Current;

			var filePath = Path.Combine( App.DocumentsFolder, "Recording.bin" );

			if ( File.Exists( filePath ) )
			{
				app.WriteLine( "...loading recording..." );

				try
				{
					using var stream = new FileStream( filePath, FileMode.Open, FileAccess.Read, FileShare.None );
					using var reader = new BinaryReader( stream );

					for ( int x = 0; x < app._ffb_recordedSteeringWheelTorqueBuffer.Length; x++ )
					{
						app._ffb_recordedSteeringWheelTorqueBuffer[ x ] = reader.ReadSingle();
					}
				}
				catch ( Exception exception )
				{
					app.WriteLine( $"Failed to load the recording file, exception was: {exception.Message.Trim()}" );
				}
			}
		}

		private static void SaveRecording()
		{
			var app = (App) Application.Current;

			app.WriteLine( "...saving recording..." );

			var filePath = Path.Combine( App.DocumentsFolder, "Recording.bin" );

			using var stream = new FileStream( filePath, FileMode.Create, FileAccess.Write, FileShare.None );
			using var writer = new BinaryWriter( stream );

			for ( int x = 0; x < app._ffb_recordedSteeringWheelTorqueBuffer.Length; x++ )
			{
				writer.Write( app._ffb_recordedSteeringWheelTorqueBuffer[ x ] );
			}
		}

		private void TogglePrettyGraph()
		{
			var app = (App) Application.Current;

			if ( app.TogglePrettyGraph() )
			{
				PrettyGraph_Border.Visibility = Visibility.Visible;

				TogglePrettyGraph_Button.Content = "Disable Pretty Graph";
			}
			else
			{
				PrettyGraph_Border.Visibility = Visibility.Collapsed;

				TogglePrettyGraph_Button.Content = "Enable Pretty Graph";
			}
		}

		#endregion

		#region Understeer effect tab

		private void UndersteerEffect_CheckBox_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "UndersteerEffect_CheckBox_Click called." );
		}

		private void SineWaveBuzz_RadioButton_Click( object sender, RoutedEventArgs e )
		{
			if ( _win_initialized )
			{
				var app = (App) Application.Current;

				app.WriteLine( "" );
				app.WriteLine( "SineWaveBuzz_RadioButton_Click called." );

				app.Settings.USEffectStyle = 0;
			}
		}

		private void SawtoothWaveBuzz_RadioButton_Click( object sender, RoutedEventArgs e )
		{
			if ( _win_initialized )
			{
				var app = (App) Application.Current;

				app.WriteLine( "" );
				app.WriteLine( "SawtoothWaveBuzz_RadioButton_Click called." );

				app.Settings.USEffectStyle = 1;
			}
		}

		private void ConstantForce_RadioButton_Click( object sender, RoutedEventArgs e )
		{
			if ( _win_initialized )
			{
				var app = (App) Application.Current;

				app.WriteLine( "" );
				app.WriteLine( "ConstantForce_RadioButton_Click called." );

				app.Settings.USEffectStyle = 2;
			}
		}

		private void UndersteerEffect_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "UndersteerEffect_Button_Click called." );

			ShowMapButtonsWindow( app.Settings.UndersteerEffectButtons );
		}

		#endregion

		#region LFE to FFB tab

		private void LFEToFFB_CheckBox_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "LFEToFFB_CheckBox_Click called." );
		}

		private void LFEDevice_ComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
		{
			if ( _win_initialized )
			{
				var app = (App) Application.Current;

				app.WriteLine( "" );
				app.WriteLine( "LFEDeviceComboBox_SelectionChanged called." );

				app.InitializeLFE();
			}
		}

		private void DecreaseLFEScale_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "DecreaseLFEScale_Button_Click called." );

			app.Settings.DecreaseLFEScaleButtons = ShowMapButtonsWindow( app.Settings.DecreaseLFEScaleButtons );
		}

		private void IncreaseLFEScale_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "IncreaseLFEScale_Button_Click called." );

			app.Settings.IncreaseLFEScaleButtons = ShowMapButtonsWindow( app.Settings.IncreaseLFEScaleButtons );
		}

		#endregion

		#region Wind simulator tab

		private void WindSimulator_CheckBox_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "WindSimulator_CheckBox_Click called." );
		}

		private void Test_CheckBox_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "Test_CheckBox_Click called." );

			if ( sender is CheckBox checkBox )
			{
				CheckBox[] testCheckBoxArray = { Test_1_CheckBox, Test_2_CheckBox, Test_3_CheckBox, Test_4_CheckBox, Test_5_CheckBox, Test_6_CheckBox, Test_7_CheckBox, Test_8_CheckBox };

				foreach ( var testCheckBox in testCheckBoxArray )
				{
					if ( testCheckBox != checkBox )
					{
						testCheckBox.IsChecked = false;
					}
				}

				var band = int.Parse( checkBox.Name.Substring( 5, 1 ) );

				if ( checkBox.IsChecked == true )
				{
					app.Wind_TestBand = band - 1;

					app.WriteLine( $"Band {band} selected for testing." );
				}
				else
				{
					app.Wind_TestBand = -1;

					app.WriteLine( $"Stopping testing on band {band}." );
				}
			}
		}

		#endregion

		#region Settings tab - Window tab

		private void TopmostWindow_CheckBox_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "TopmostWindow_CheckBox_Click called." );

			var checkBox = (CheckBox) sender;

			Topmost = checkBox.IsChecked == true;
		}

		#endregion

		#region Settings tab - Save file tab

		private void SaveForEachWheel_CheckBox_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "SaveForEachWheel_CheckBox_Click called." );

			app.UpdateWheelSaveName();
			app.QueueForSerialization();
		}

		private void SaveForEachCar_CheckBox_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "SaveForEachCar_CheckBox_Click called." );

			app.UpdateCarSaveName();
			app.QueueForSerialization();
		}

		private void SaveForEachTrack_CheckBox_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "SaveForEachTrack_CheckBox_Click called." );

			app.UpdateTrackSaveName();
			app.QueueForSerialization();
		}

		private void SaveForEachTrackConfig_CheckBox_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "SaveForEachTrackConfig_CheckBox_Click called." );

			app.UpdateTrackConfigSaveName();
			app.QueueForSerialization();
		}

		#endregion

		#region Settings tab - Audio tab

		private void ClickSoundVolume_Slider_ValueChanged( object sender, RoutedPropertyChangedEventArgs<double> e )
		{
			if ( _win_initialized )
			{
				var app = (App) Application.Current;

				app.WriteLine( "" );
				app.WriteLine( "ClickSoundVolume_Slider_ValueChanged called." );

				app.PlayClick();
			}
		}

		#endregion

		#region Settings tab - Voice tab

		private void SpeechSynthesizerVolume_Slider_ValueChanged( object sender, RoutedPropertyChangedEventArgs<double> e )
		{
			if ( _win_initialized )
			{
				var app = (App) Application.Current;

				app.WriteLine( "" );
				app.WriteLine( "SpeechSynthesizerVolume_Slider_ValueChanged called." );

				app.UpdateVolume();

				app.Say( app.Settings.SayVoiceVolume, app.Settings.SpeechSynthesizerVolume.ToString(), true );
			}
		}

		private void SelectedVoice_ComboBox_SelectionChanged( object sender, RoutedEventArgs e )
		{
			if ( _win_initialized )
			{
				var app = (App) Application.Current;

				app.WriteLine( "" );
				app.WriteLine( "SelectedVoice_ComboBox_SelectionChanged called." );

				app.InitializeVoice();

				app.Say( app.Settings.SayHello, null, true );
			}
		}

		#endregion

		#region Settings tab - Wheel tab

		private void SelectedWheelAxis_ComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
		{
			if ( _win_initialized )
			{
				var app = (App) Application.Current;

				app.WriteLine( "" );
				app.WriteLine( "SelectedWheelAxis_ComboBox_SelectionChanged called." );
			}
		}

		private void SetWheelMinValue_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "SetWheelMinValue_Button_Click called." );

			app.Settings.WheelMinValue = app.Input_CurrentWheelPosition;
		}

		private void SetWheelCenterValue_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "SetWheelCenterValue_Button_Click called." );

			app.Settings.WheelCenterValue = app.Input_CurrentWheelPosition;
		}

		private void SetWheelMaxValue_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "SetWheelMaxValue_Button_Click called." );

			app.Settings.WheelMaxValue = app.Input_CurrentWheelPosition;
		}

		private void AutoCenterWheel_CheckBox_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "AutoCenterWheel_CheckBox_Click called." );
		}

		private void AutoCenterWheelStrength_Slider_ValueChanged( object sender, RoutedPropertyChangedEventArgs<double> e )
		{
			if ( _win_initialized )
			{
				var app = (App) Application.Current;

				app.WriteLine( "" );
				app.WriteLine( "AutoCenterWheelStrength_Slider_ValueChanged called." );
			}
		}

		#endregion

		#region Help tab

		private void SeeHelpDocumentation_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "SeeHelpDocumentation_Click called." );

			string url = "https://herboldracing.com/marvins-awesome-iracing-app-maira/";

			var processStartInfo = new ProcessStartInfo( "cmd", $"/c start {url}" )
			{
				CreateNoWindow = true
			};

			Process.Start( processStartInfo );
		}

		private void GoToIRacingForumThread_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "GoToIRacingForumThread_Click called." );

			string url = "https://forums.iracing.com/discussion/72467/marvins-awesome-iracing-app";

			var processStartInfo = new ProcessStartInfo( "cmd", $"/c start {url}" )
			{
				CreateNoWindow = true
			};

			Process.Start( processStartInfo );
		}

		private void SendMarvinYourConsoleLog_Button_Click( object sender, RoutedEventArgs e )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "SendMarvinYourConsoleLog_Click called." );

			var text = Console_TextBox.Text.Replace( "\r\n", "\r\n\t" );

			Clipboard.SetText( $"\r\n\r\n\t{text}\r\n" );

			string url = "https://forums.iracing.com/messages/add/Marvin%20Herbold";

			var processStartInfo = new ProcessStartInfo( "cmd", $"/c start {url}" )
			{
				CreateNoWindow = true
			};

			Process.Start( processStartInfo );
		}

		#endregion

		#region Map button window

		private Settings.MappedButtons ShowMapButtonsWindow( Settings.MappedButtons mappedButtons )
		{
			var app = (App) Application.Current;

			app.WriteLine( "" );
			app.WriteLine( "Showing the map buttons dialog window..." );

			_win_pauseButtons = true;

			var window = new MapButtonWindow
			{
				Owner = this,
				MappedButtons = mappedButtons,
			};

			window.ShowDialog();

			if ( !window.canceled )
			{
				app.WriteLine( "...dialog window was closed..." );

				mappedButtons.Button1 = window.MappedButtons.Button1;
				mappedButtons.Button2 = window.MappedButtons.Button2;

				app.QueueForSerialization();

				app.WriteLine( $"...button mapping was changed." );
			}
			else
			{
				app.WriteLine( "...dialog window was closed (canceled)." );
			}

			_win_pauseButtons = false;

			return mappedButtons;
		}

		#endregion
	}
}
