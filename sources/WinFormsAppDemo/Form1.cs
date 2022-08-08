using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinFormsAppDemo {
	public partial class Form1 : Form {
		private int lineCount = 0;

		// Sorts ListViewItem objects by index.
		private class ListViewIndexComparer : System.Collections.IComparer {
			public int Compare(object x, object y) {
				return ( ( ListViewItem ) x ).Index - ( ( ListViewItem ) y ).Index;
			}
		}

		public Form1() {
			this.InitializeComponent();
		}

		private void Form1_Load(object sender, EventArgs e) {
			//this.myListView.Dock = DockStyle.Fill;
			this.myListView.View = View.Details;
			this.myListView.MultiSelect = false;
			this.myListView.ListViewItemSorter = new ListViewIndexComparer();

			// Initialize the insertion mark.
			this.myListView.InsertionMark.Color = Color.Green;

			// Add items to myListView.
			ListViewItem item = this.myListView.Items.Add( "zero" );

			this.myListView.Items.Add( "one" );
			this.myListView.Items.Add( "two" );
			this.myListView.Items.Add( "three" );
			this.myListView.Items.Add( "four" );
			this.myListView.Items.Add( "five" );

			//todo: deslocar a atribuição da pintura para o momento da carga do item e novamente quando houver alteração de seu estado
			foreach (ListViewItem paintItem	 in this.myListView.Items) {
				//Inform correct color to paint item
				paintItem.UseItemStyleForSubItems = false;
				this.lineCount++;
				if (this.lineCount % 2 == 0) {
					paintItem.BackColor = Color.LightBlue;
				}
			}

			// Initialize the drag-and-drop operation when running
			// under Windows XP or a later operating system.
			if (OSFeature.Feature.IsPresent( OSFeature.Themes )) {
				this.myListView.AllowDrop = true;
				this.myListView.ItemDrag += new ItemDragEventHandler( this.MyListView_ItemDrag );
				this.myListView.DragEnter += new DragEventHandler( this.MyListView_DragEnter );
				this.myListView.DragOver += new DragEventHandler( this.MyListView_DragOver );
				this.myListView.DragLeave += new EventHandler( this.MyListView_DragLeave );
				this.myListView.DragDrop += new DragEventHandler( this.MyListView_DragDrop );
			}

			// Initialize the form.
			this.Text = "ListView Insertion Mark Example";

			
			
		}

		private void MyListView_ItemDrag(object sender, ItemDragEventArgs e) {
			this.myListView.DoDragDrop( e.Item, DragDropEffects.Move );
		}

		// Sets the target drop effect.
		private void MyListView_DragEnter(object sender, DragEventArgs e) {
			e.Effect = e.AllowedEffect;
		}

		// Moves the insertion mark as the item is dragged.
		private void MyListView_DragOver(object sender, DragEventArgs e) {
			// Retrieve the client coordinates of the mouse pointer.
			Point targetPoint =
				this.myListView.PointToClient( new Point( e.X, e.Y ) );

			// Retrieve the index of the item closest to the mouse pointer.
			int targetIndex = this.myListView.InsertionMark.NearestIndex( targetPoint );

			// Confirm that the mouse pointer is not over the dragged item.
			if (targetIndex > -1) {
				// Determine whether the mouse pointer is to the left or
				// the right of the midpoint of the closest item and set
				// the InsertionMark.AppearsAfterItem property accordingly.
				Rectangle itemBounds = this.myListView.GetItemRect( targetIndex );
				if (targetPoint.X > itemBounds.Left + ( itemBounds.Width / 2 )) {
					this.myListView.InsertionMark.AppearsAfterItem = true;
				} else {
					this.myListView.InsertionMark.AppearsAfterItem = false;
				}
			}

			// Set the location of the insertion mark. If the mouse is
			// over the dragged item, the targetIndex value is -1 and
			// the insertion mark disappears.
			this.myListView.InsertionMark.Index = targetIndex;
		}

		// Removes the insertion mark when the mouse leaves the control.
		private void MyListView_DragLeave(object sender, EventArgs e) {
			this.myListView.InsertionMark.Index = -1;
		}

		// Moves the item to the location of the insertion mark.
		private void MyListView_DragDrop(object sender, DragEventArgs e) {
			// Retrieve the index of the insertion mark;
			int targetIndex = this.myListView.InsertionMark.Index;

			// If the insertion mark is not visible, exit the method.
			if (targetIndex == -1) {
				return;
			}

			// If the insertion mark is to the right of the item with
			// the corresponding index, increment the target index.
			if (this.myListView.InsertionMark.AppearsAfterItem) {
				targetIndex++;
			}

			// Retrieve the dragged item.
			ListViewItem draggedItem = ( ListViewItem ) e.Data.GetData( typeof( ListViewItem ) );

			// Insert a copy of the dragged item at the target index.
			// A copy must be inserted before the original item is removed
			// to preserve item index values. 
			this.myListView.Items.Insert( targetIndex, ( ListViewItem ) draggedItem.Clone() );

			// Remove the original copy of the dragged item.
			this.myListView.Items.Remove( draggedItem );
		}
		// Starts the drag-and-drop operation when an item is dragged.

	}

}
