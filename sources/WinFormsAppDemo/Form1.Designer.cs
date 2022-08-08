
namespace WinFormsAppDemo {
	partial class Form1 {
		/// <summary>
		///  Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		///  Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing) {
			if (disposing && ( components != null )) {
				components.Dispose();
			}
			base.Dispose( disposing );
		}

		#region Windows Form Designer generated code

		/// <summary>
		///  Required method for Designer support - do not modify
		///  the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent() {
			this.myListView = new System.Windows.Forms.ListView();
			this.columnHeader1 = new System.Windows.Forms.ColumnHeader();
			this.SuspendLayout();
			// 
			// myListView
			// 
			this.myListView.AutoArrange = false;
			this.myListView.CheckBoxes = true;
			this.myListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1});
			this.myListView.FullRowSelect = true;
			this.myListView.HideSelection = false;
			this.myListView.Location = new System.Drawing.Point(82, 87);
			this.myListView.Name = "myListView";
			this.myListView.Size = new System.Drawing.Size(636, 311);
			this.myListView.TabIndex = 0;
			this.myListView.UseCompatibleStateImageBehavior = false;
			this.myListView.View = System.Windows.Forms.View.Details;
			// 
			// columnHeader1
			// 
			this.columnHeader1.Tag = "55656";
			this.columnHeader1.Text = "ColumnHeader-888";
			// 
			// Form1
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(800, 450);
			this.Controls.Add(this.myListView);
			this.Name = "Form1";
			this.Text = "Form1";
			this.Load += new System.EventHandler(this.Form1_Load);
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.ListView myListView;
		private System.Windows.Forms.ColumnHeader columnHeader1;
	}
}

