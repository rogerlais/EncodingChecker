using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Deployment.Application;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace EncodingChecker {
	public partial class MainForm : Form {
		private sealed class WorkerArgs {
			internal CurrentAction Action;
			internal string BaseDirectory;
			internal bool IncludeSubdirectories;
			internal string FileMasks;
			internal List<string> ValidCharsets;
		}

		private sealed class WorkerProgress {
			internal string FileName;
			internal string FileExt;
			internal string DirectoryName;
			internal string Charset;
		}

		private enum CurrentAction {
			View,
			Validate,
			Convert,
		}

		private readonly ListViewColumnSorter _lvwColumnSorter;

		private readonly BackgroundWorker _actionWorker;
		private CurrentAction _currentAction;
		private Settings _settings;
		private ListViewItem heldDownItem;
		private Point heldDownPoint;
		private const int RESULTS_COLUMN_CHARSET = 0;
		private const int RESULTS_COLUMN_FILE_NAME = 1;
		private const int RESULTS_COLUMN_FILE_EXT = 2;
		private const int RESULTS_COLUMN_DIRECTORY = 3;

		public MainForm() {
			this.InitializeComponent();

			this._lvwColumnSorter = new ListViewColumnSorter();
			this.lstResults.ListViewItemSorter = this._lvwColumnSorter;

			this._actionWorker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
			this._actionWorker.DoWork += ActionWorkerDoWork;
			this._actionWorker.ProgressChanged += this.ActionWorkerProgressChanged;
			this._actionWorker.RunWorkerCompleted += this.ActionWorkerCompleted;
		}

		#region Form events
		private void OnFormLoad(object sender, EventArgs e) {
			this.lstConvert.BeginUpdate();

			IEnumerable<string> validCharsets = GetSupportedCharsets();
			foreach (string validCharset in validCharsets) {
				try {   // add only those charsets which are supported by .NET
					Encoding encoding = Encoding.GetEncoding( validCharset );
					this.lstValidCharsets.Items.Add( encoding.WebName );
					this.lstConvert.Items.Add( encoding.WebName );
					// add UTF-8/16 with BOM, right after UTF-8/16
					const string pattern = "^utf-16BE|utf-16|utf-8$";
					if (Regex.IsMatch( encoding.WebName, pattern )) {
						this.lstValidCharsets.Items.Add( encoding.WebName + "-bom" );
						this.lstConvert.Items.Add( encoding.WebName + "-bom" );
					}
				} catch {
					// ignored charsets
				}
			}
			if (this.lstConvert.Items.Count > 0)
				this.lstConvert.SelectedIndex = 0;

			this.lstConvert.EndUpdate();

			this.btnView.Tag = CurrentAction.View;
			this.btnValidate.Tag = CurrentAction.Validate;
			this.btnConvert.Tag = CurrentAction.Convert;

			this.LoadSettings();

			//Size the result list columns based on the initial size of the window
			this.lstResults.Columns[RESULTS_COLUMN_CHARSET].AutoResize( ColumnHeaderAutoResizeStyle.HeaderSize );
			int remainingWidth = this.lstResults.Width - this.lstResults.Columns[0].Width;
			this.lstResults.Columns[RESULTS_COLUMN_FILE_NAME].Width = ( 30 * remainingWidth ) / 100;
			this.lstResults.Columns[RESULTS_COLUMN_FILE_EXT].AutoResize( ColumnHeaderAutoResizeStyle.HeaderSize );
			this.lstResults.Columns[RESULTS_COLUMN_DIRECTORY].AutoResize( ColumnHeaderAutoResizeStyle.HeaderSize );
		}

		private void OnFormClosing(object sender, FormClosingEventArgs e) {
			this.SaveSettings();
		}

		private void OnBrowseDirectories(object sender, EventArgs e) {
			this.dlgBrowseDirectories.SelectedPath = this.lstBaseDirectory.Text;
			if (this.dlgBrowseDirectories.ShowDialog( this ) == DialogResult.OK) {
				this.lstBaseDirectory.Text = this.dlgBrowseDirectories.SelectedPath;
				this.lstBaseDirectory.Items.Add( this.dlgBrowseDirectories.SelectedPath );
			}
		}

		private void OnSelectDeselectAll(object sender, EventArgs e) {
			this.lstResults.ItemChecked -= this.OnResultItemChecked;
			try {
				bool isChecked = this.chkSelectDeselectAll.Checked;
				foreach (ListViewItem item in this.lstResults.Items)
					item.Checked = isChecked;
			} finally {
				this.lstResults.ItemChecked += this.OnResultItemChecked;
			}
		}

		private void OnResultItemChecked(object sender, ItemCheckedEventArgs e) {
			this.chkSelectDeselectAll.CheckedChanged -= this.OnSelectDeselectAll;
			try {
				if (this.lstResults.CheckedItems.Count == 0)
					this.chkSelectDeselectAll.CheckState = CheckState.Unchecked;
				else if (this.lstResults.CheckedItems.Count == this.lstResults.Items.Count)
					this.chkSelectDeselectAll.CheckState = CheckState.Checked;
				else
					this.chkSelectDeselectAll.CheckState = CheckState.Indeterminate;
			} finally {
				this.chkSelectDeselectAll.CheckedChanged += this.OnSelectDeselectAll;
			}
		}
		private void OnResultColumnClick(object o, ColumnClickEventArgs e) {
			if (e.Column == this._lvwColumnSorter.SortColumn) {
				this._lvwColumnSorter.Order = this._lvwColumnSorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
			} else {
				this._lvwColumnSorter.SortColumn = e.Column;
				this._lvwColumnSorter.Order = SortOrder.Ascending;
			}
			this.lstResults.Sort();
			this.lstResults.SetSortIcon( this._lvwColumnSorter.SortColumn, this._lvwColumnSorter.Order );
		}

		private void OnHelp(object sender, EventArgs e) {
			ProcessStartInfo psi =
				new ProcessStartInfo( "https://github.com/amrali-eg/EncodingChecker" ) { UseShellExecute = true };
			Process.Start( psi );
		}

		private void OnAbout(object sender, EventArgs e) {
			using (AboutForm aboutForm = new AboutForm())
				aboutForm.ShowDialog( this );
		}

		private void OnExport(object sender, EventArgs e) {
			if (this.lstResults.CheckedItems.Count <= 0) {
				this.ShowWarning( "Select one or more files to export" );
				return;
			}

			string filename1 = "";
			SaveFileDialog saveFileDialog1 = new SaveFileDialog {
				Title = "Export to a Text File",
				Filter = "txt files (*.txt)|*.txt",
				RestoreDirectory = true
			};
			if (saveFileDialog1.ShowDialog() == DialogResult.OK) {
				filename1 = saveFileDialog1.FileName;
			}

			if (filename1 != "") {
				try {
					using (StreamWriter sw = new StreamWriter( filename1 )) {
						foreach (ListViewItem item in this.lstResults.CheckedItems) {
							string charset = item.SubItems[RESULTS_COLUMN_CHARSET].Text;
							string fileName = item.SubItems[RESULTS_COLUMN_FILE_NAME].Text;
							string directory = item.SubItems[RESULTS_COLUMN_DIRECTORY].Text;
							sw.WriteLine( "{0}\t{1}\\{2}", charset, directory, fileName );
						}
					}
				} catch {
					// do nothing
				}
			}
		}
		#endregion

		#region Action button handling
		private void OnAction(object sender, EventArgs e) {
			CurrentAction action = ( CurrentAction ) ( ( Button ) sender ).Tag;
			this.StartAction( action );
		}

		private void StartAction(CurrentAction action) {
			string directory = this.lstBaseDirectory.Text;
			if (string.IsNullOrEmpty( directory )) {
				this.ShowWarning( "Please specify a directory to check" );
				return;
			}
			if (!Directory.Exists( directory )) {
				this.ShowWarning( "The directory you specified '{0}' does not exist", directory );
				return;
			}
			if (action == CurrentAction.Validate && this.lstValidCharsets.CheckedItems.Count == 0) {
				this.ShowWarning( "Select one or more valid character sets to proceed with validation" );
				return;
			}

			this._currentAction = action;

			if (this._settings == null)
				this._settings = new Settings();
			this._settings.RecentDirectories.Add( directory );

			this.UpdateControlsOnActionStart();

			List<string> validCharsets = new List<string>( this.lstValidCharsets.CheckedItems.Count );
			foreach (string validCharset in this.lstValidCharsets.CheckedItems)
				validCharsets.Add( validCharset );

			WorkerArgs args = new WorkerArgs {
				Action = action,
				BaseDirectory = directory,
				IncludeSubdirectories = this.chkIncludeSubdirectories.Checked,
				FileMasks = this.txtFileMasks.Text,
				ValidCharsets = validCharsets
			};
			this._actionWorker.RunWorkerAsync( args );
		}

		private void OnConvert(object sender, EventArgs e) {
			if (this.lstResults.CheckedItems.Count == 0) {
				this.ShowWarning( "Select one or more files to convert" );
				return;
			}

			// stop drawing of the results list view control
			this.lstResults.BeginUpdate();
			this.lstResults.ItemChecked -= this.OnResultItemChecked;

			foreach (ListViewItem item in this.lstResults.CheckedItems) {
				string charset = item.SubItems[RESULTS_COLUMN_CHARSET].Text;
				if (charset == "(Unknown)")
					continue;

				if (charset.EndsWith( "-bom" ))
					charset = charset.Replace( "-bom", "" );

				string fileName = item.SubItems[RESULTS_COLUMN_FILE_NAME].Text;
				string directory = item.SubItems[RESULTS_COLUMN_DIRECTORY].Text;
				string filePath = Path.Combine( directory, fileName );

				try {
					FileAttributes attributes = File.GetAttributes( filePath );
					if (( attributes & FileAttributes.ReadOnly ) == FileAttributes.ReadOnly) {
						attributes ^= FileAttributes.ReadOnly;
						File.SetAttributes( filePath, attributes );
					}

					if (!Encoding.GetEncoding( charset ).Validate( File.ReadAllBytes( filePath ) )) {
						Debug.WriteLine( "Decoding error. " + filePath );
						continue;
					}

					string content;
					using (StreamReader reader = new StreamReader( filePath, Encoding.GetEncoding( charset ) ))
						content = reader.ReadToEnd();

					string targetCharset = ( string ) this.lstConvert.SelectedItem;
					Encoding encoding;
					// handle UTF-8/16 and UTF-8/16 with BOM
					switch (targetCharset) {
						case "utf-8":
							encoding = new UTF8Encoding( false );
							break;
						case "utf-8-bom":
							encoding = new UTF8Encoding( true );
							break;
						case "utf-16":
							encoding = new UnicodeEncoding( bigEndian: false, byteOrderMark: false );
							break;
						case "utf-16-bom":
							encoding = new UnicodeEncoding( bigEndian: false, byteOrderMark: true );
							break;
						case "utf-16BE":
							encoding = new UnicodeEncoding( bigEndian: true, byteOrderMark: false );
							break;
						case "utf-16BE-bom":
							encoding = new UnicodeEncoding( bigEndian: true, byteOrderMark: true );
							break;
						default:
							encoding = Encoding.GetEncoding( targetCharset );
							break;
					}

					using (StreamWriter writer = new StreamWriter( filePath, append: false, encoding )) {
						writer.Write( content );
						writer.Flush();
					}

					item.Checked = false;
					item.ImageIndex = 0;
					item.SubItems[RESULTS_COLUMN_CHARSET].Text = targetCharset;
				} catch {
					// do nothing
				}
			}

			// resume drawing of the results list view control
			this.lstResults.ItemChecked += this.OnResultItemChecked;
			this.lstResults.EndUpdate();

			// execute handler of the 'ItemChecked' event
			this.OnResultItemChecked( this.lstResults, new ItemCheckedEventArgs( this.lstResults.Items[0] ) );
		}

		private void OnCancelAction(object sender, EventArgs e) {
			if (this._actionWorker.IsBusy) {
				this.btnCancel.Visible = false;
				this._actionWorker.CancelAsync();
			}
		}
		#endregion

		#region Background worker event handlers and helper methods
		private static void ActionWorkerDoWork(object sender, DoWorkEventArgs e) {
			const int progressBufferSize = 5;

			BackgroundWorker worker = ( BackgroundWorker ) sender;
			WorkerArgs args = ( WorkerArgs ) e.Argument;

			string[] allFiles = Directory.GetFiles( args.BaseDirectory, "*.*",
				args.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly );

			WorkerProgress[] progressBuffer = new WorkerProgress[progressBufferSize];
			int reportBufferCounter = 1;

			IEnumerable<Regex> maskPatterns = GenerateMaskPatterns( args.FileMasks );
			for (int i = 0; i < allFiles.Length; i++) {
				if (worker.CancellationPending) {
					e.Cancel = true;
					break;
				}

				string path = allFiles[i];
				string fileName = Path.GetFileName( path );
				if (!SatisfiesMaskPatterns( fileName, maskPatterns ))
					continue;

				bool hasBOM = false;
				Encoding encoding = TextEncoding.GetFileEncoding( path, ref hasBOM );
				string charset = encoding?.WebName ?? "(Unknown)";
				if (hasBOM) {
					charset += "-bom";
				}

				if (args.Action == CurrentAction.Validate && args.ValidCharsets.Contains( charset ))
					continue;

				string directoryName = Path.GetDirectoryName( path );
				string fileExt = Path.GetExtension( path );

				progressBuffer[reportBufferCounter - 1] = new WorkerProgress {
					Charset = charset,
					FileName = fileName,
					FileExt = fileExt,
					DirectoryName = directoryName
				};
				reportBufferCounter++;
				if (reportBufferCounter > progressBufferSize) {
					reportBufferCounter = 1;
					int percentageCompleted = ( i * 100 ) / allFiles.Length;
					WorkerProgress[] reportProgress = new WorkerProgress[progressBufferSize];
					Array.Copy( progressBuffer, reportProgress, progressBufferSize );
					worker.ReportProgress( percentageCompleted, reportProgress );
					Array.Clear( progressBuffer, 0, progressBufferSize );
				}
			}

			// Copy remaining results from buffer, if any.
			if (reportBufferCounter > 1) {
				reportBufferCounter--;
				const int percentageCompleted = 100;
				WorkerProgress[] reportProgress = new WorkerProgress[reportBufferCounter];
				Array.Copy( progressBuffer, reportProgress, reportBufferCounter );
				worker.ReportProgress( percentageCompleted, reportProgress );
				Array.Clear( progressBuffer, 0, reportBufferCounter );
			}
		}

		private static IEnumerable<Regex> GenerateMaskPatterns(string fileMaskString) {
			string[] fileMasks = fileMaskString.Split( new[] { Environment.NewLine },
				StringSplitOptions.RemoveEmptyEntries );
			string[] processedFileMasks = Array.FindAll( fileMasks, mask => mask.Trim().Length > 0 );
			if (processedFileMasks.Length == 0)
				processedFileMasks = new[] { "*.*" };

			List<Regex> maskPatterns = new List<Regex>( processedFileMasks.Length );
			foreach (string fileMask in processedFileMasks) {
				if (string.IsNullOrEmpty( fileMask ))
					continue;
				Regex maskPattern =
					new Regex( "^" + fileMask.Replace( ".", "[.]" ).Replace( "*", ".*" ).Replace( "?", "." ) + "$",
						RegexOptions.IgnoreCase );
				maskPatterns.Add( maskPattern );
			}
			return maskPatterns;
		}

		private static bool SatisfiesMaskPatterns(string fileName, IEnumerable<Regex> maskPatterns) {
			foreach (Regex maskPattern in maskPatterns) {
				if (maskPattern.IsMatch( fileName ))
					return true;
			}
			return false;
		}

		private void ActionWorkerProgressChanged(object sender, ProgressChangedEventArgs e) {
			WorkerProgress[] progresses = ( WorkerProgress[] ) e.UserState;

			foreach (WorkerProgress progress in progresses) {
				if (progress == null)
					break;
				ListViewItem resultItem = new ListViewItem( new[] { progress.Charset, progress.FileName, progress.FileExt, progress.DirectoryName }, -1 );
				this.lstResults.Items.Add( resultItem );
				this.actionStatus.Text = progress.FileName;
			}

			this.actionProgress.Value = e.ProgressPercentage;
		}

		private void ActionWorkerCompleted(object sender, RunWorkerCompletedEventArgs e) {
			if (this.lstResults.Items.Count > 0) {
				foreach (ColumnHeader columnHeader in this.lstResults.Columns)
					columnHeader.AutoResize( ColumnHeaderAutoResizeStyle.ColumnContent );
			}
			this.UpdateControlsOnActionDone();
		}
		#endregion

		#region Loading and saving of settings
		private void LoadSettings() {
			string settingsFileName = GetSettingsFileName();
			if (!File.Exists( settingsFileName ))
				return;
			using (FileStream settingsFile = new FileStream( settingsFileName, FileMode.Open, FileAccess.Read, FileShare.Read )) {
				BinaryFormatter formatter = new BinaryFormatter();
				object settingsInstance = formatter.Deserialize( settingsFile );
				this._settings = ( Settings ) settingsInstance;
			}

			if (this._settings.RecentDirectories?.Count > 0) {
				foreach (string recentDirectory in this._settings.RecentDirectories)
					this.lstBaseDirectory.Items.Add( recentDirectory );
				this.lstBaseDirectory.SelectedIndex = 0;
			} else
				this.lstBaseDirectory.Text = Environment.CurrentDirectory;
			this.chkIncludeSubdirectories.Checked = this._settings.IncludeSubdirectories;
			this.txtFileMasks.Text = this._settings.FileMasks;
			if (this._settings.ValidCharsets?.Length > 0) {
				for (int i = 0; i < this.lstValidCharsets.Items.Count; i++)
					if (Array.Exists( this._settings.ValidCharsets,
						charset => charset.Equals( ( string ) this.lstValidCharsets.Items[i] ) ))
						this.lstValidCharsets.SetItemChecked( i, true );
			}

			this._settings.WindowPosition?.ApplyTo( this );
		}

		private void SaveSettings() {
			if (this._settings == null)
				this._settings = new Settings();
			this._settings.IncludeSubdirectories = this.chkIncludeSubdirectories.Checked;
			this._settings.FileMasks = this.txtFileMasks.Text;

			this._settings.ValidCharsets = new string[this.lstValidCharsets.CheckedItems.Count];
			for (int i = 0; i < this.lstValidCharsets.CheckedItems.Count; i++)
				this._settings.ValidCharsets[i] = ( string ) this.lstValidCharsets.CheckedItems[i];

			this._settings.WindowPosition = new WindowPosition { Left = Left, Top = Top, Width = Width, Height = Height };

			string settingsFileName = GetSettingsFileName();
			using (
				FileStream settingsFile = new FileStream( settingsFileName, FileMode.Create, FileAccess.Write,
					FileShare.None )) {
				BinaryFormatter formatter = new BinaryFormatter();
				formatter.Serialize( settingsFile, this._settings );
				settingsFile.Flush();
			}
		}

		private static string GetSettingsFileName() {
			string dataDirectory = ApplicationDeployment.IsNetworkDeployed
				? ApplicationDeployment.CurrentDeployment.DataDirectory
				: Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData );
			if (string.IsNullOrEmpty( dataDirectory ) || !Directory.Exists( dataDirectory ))
				dataDirectory = Environment.CurrentDirectory;
			dataDirectory = Path.Combine( dataDirectory, "EncodingChecker" );
			if (!Directory.Exists( dataDirectory ))
				Directory.CreateDirectory( dataDirectory );
			return Path.Combine( dataDirectory, "Settings.bin" );
		}
		#endregion

		private void UpdateControlsOnActionStart() {
			this.btnView.Enabled = false;
			this.btnValidate.Enabled = false;

			this.lblConvert.Enabled = false;
			this.lstConvert.Enabled = false;
			this.btnConvert.Enabled = false;
			this.chkSelectDeselectAll.Enabled = false;
			this.chkSelectDeselectAll.CheckState = CheckState.Unchecked;

			this.btnCancel.Visible = true;

			// stop drawing of the results list view control
			this.lstResults.BeginUpdate();
			this.lstResults.ListViewItemSorter = null;
			this.lstResults.ItemChecked -= this.OnResultItemChecked;
			this.lstResults.Items.Clear();

			this.actionProgress.Value = 0;
			this.actionProgress.Visible = true;
			this.actionStatus.Text = string.Empty;
		}

		private void UpdateControlsOnActionDone() {
			this.btnView.Enabled = true;
			this.btnValidate.Enabled = true;

			if (this.lstResults.Items.Count > 0) {
				this.lblConvert.Enabled = true;
				this.lstConvert.Enabled = true;
				this.btnConvert.Enabled = true;
				this.chkSelectDeselectAll.Enabled = true;

				if (this._currentAction == CurrentAction.Validate && this.lstValidCharsets.CheckedItems.Count > 0) {
					string firstValidCharset = ( string ) this.lstValidCharsets.CheckedItems[0];
					for (int i = 0; i < this.lstConvert.Items.Count; i++) {
						string convertCharset = ( string ) this.lstConvert.Items[i];
						if (firstValidCharset.Equals( convertCharset, StringComparison.OrdinalIgnoreCase )) {
							this.lstConvert.SelectedIndex = i;
							break;
						}
					}
				}
			}

			this.btnCancel.Visible = false;

			// resume drawing of the results list view control
			this.lstResults.ListViewItemSorter = this._lvwColumnSorter;
			this.lstResults.ItemChecked += this.OnResultItemChecked;
			this.lstResults.Sort();
			this.lstResults.EndUpdate();

			this.actionProgress.Visible = false;

			string statusMessage = this._currentAction == CurrentAction.View
				? "{0} files processed" : "{0} files do not have the correct encoding";
			this.actionStatus.Text = string.Format( statusMessage, this.lstResults.Items.Count );
		}

		private static IEnumerable<string> GetSupportedCharsets() {
			//Using reflection, figure out all the charsets that the UtfUnknown framework supports by reflecting
			//over all the strings constants in the UtfUnknown.Core.CodepageName class. These represent all the encodings
			//that can be detected by the program.
			Type codepageName = typeof( UtfUnknown.Core.CodepageName );
			FieldInfo[] charsetConstants = codepageName.GetFields( BindingFlags.GetField | BindingFlags.Static | BindingFlags.Public );
			foreach (FieldInfo charsetConstant in charsetConstants) {
				if (charsetConstant.FieldType == typeof( string ))
					yield return ( string ) charsetConstant.GetValue( null );
			}
		}

		private void ShowWarning(string message, params object[] args) {
			MessageBox.Show( this, string.Format( message, args ), @"Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning );
		}

		private void LstResults_MouseDown(object sender, MouseEventArgs e) {
			//negate below
			this.lstResults.AutoArrange = false;

			this.heldDownItem = this.lstResults.GetItemAt( e.X, e.Y );
			if (this.heldDownItem != null) {
				this.heldDownPoint = new System.Drawing.Point( e.X - this.heldDownItem.Position.X,
										  e.Y - this.heldDownItem.Position.Y );
			}
		}

		private void LstResults_MouseMove(object sender, MouseEventArgs e) {
			if (this.heldDownItem != null) {
				this.heldDownItem.Position = new Point( e.Location.X - this.heldDownPoint.X, e.Location.Y - this.heldDownPoint.Y );
			}
		}

		private void LstResults_MouseUp(object sender, MouseEventArgs e) {
			this.heldDownItem = null;
			//negate below
			this.lstResults.AutoArrange = true;
		}
	}
}