//
// NavigationHistoryManager.cs
//
// Author:
//       Lluis Sanchez <llsan@microsoft.com>
//
// Copyright (c) 2019 Microsoft
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MonoDevelop.Core;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Ide.Gui.Content;
using MonoDevelop.Ide.Gui.Documents;
using MonoDevelop.Ide.TextEditing;
using MonoDevelop.Projects;

namespace MonoDevelop.Ide.Navigation
{
	[DefaultServiceImplementation]
	public class NavigationHistoryService: IService
	{
		HistoryList history = new HistoryList ();
		List<Tuple<NavigationPoint, int>> closedHistory = new List<Tuple<NavigationPoint, int>> ();
		DocumentManager documentManager;
		RootWorkspace workspace;
		TextEditorService textEditorService;

		//used to prevent re-logging the current point during a switch
		bool switching;

		//whethor the current node is transient. Prevents excession automatic logging when switching rapidly between 
		//documents
		bool currentIsTransient;

		//the amount of time until a "transient" current node becomes "permanent"
		uint TRANSIENT_TIMEOUT = 10000; //ms

		Document currentDoc;

		Task IService.Dispose ()
		{
			workspace.LastWorkspaceItemClosed -= Workspace_LastWorkspaceItemClosed;
			workspace.FileRenamedInProject -= FileRenamed;

			documentManager.DocumentOpened -= DocumentManager_DocumentOpened;
			documentManager.DocumentClosing -= DocumentManager_DocumentClosing;
			documentManager.ActiveDocumentChanged -= ActiveDocChanged;

			textEditorService.LineCountChanged -= LineCountChanged;
			textEditorService.LineCountChangesCommitted -= CommitCountChanges;
			textEditorService.LineCountChangesReset -= ResetCountChanges;
			return Task.CompletedTask;
		}

		async Task IService.Initialize (ServiceProvider serviceProvider)
		{
			workspace = await serviceProvider.GetService<RootWorkspace> ();
			workspace.LastWorkspaceItemClosed += Workspace_LastWorkspaceItemClosed;
			workspace.FileRenamedInProject += FileRenamed;

			documentManager = await serviceProvider.GetService<DocumentManager> ();
			documentManager.DocumentOpened += DocumentManager_DocumentOpened;
			documentManager.DocumentClosing += DocumentManager_DocumentClosing;
			documentManager.ActiveDocumentChanged += ActiveDocChanged;

			//keep nav points up to date
			textEditorService = await serviceProvider.GetService<TextEditorService> ();
			textEditorService.LineCountChanged += LineCountChanged;
			textEditorService.LineCountChangesCommitted += CommitCountChanges;
			textEditorService.LineCountChangesReset += ResetCountChanges;
		}

		void Workspace_LastWorkspaceItemClosed (object sender, EventArgs e)
		{
			Reset ();
		}

		void DocumentManager_DocumentOpened (object sender, DocumentEventArgs e)
		{
			closedHistory.RemoveAll (np => (np.Item1 as DocumentNavigationPoint)?.FileName == e.Document.FileName);
			OnClosedHistoryChanged ();
		}

		Task DocumentManager_DocumentClosing (object sender, DocumentCloseEventArgs e)
		{
			NavigationPoint point = GetNavPointForDoc (e.Document, true) as DocumentNavigationPoint;
			if (point == null)
				return Task.CompletedTask;

			closedHistory.Add (new Tuple<NavigationPoint, int> (point, documentManager.Documents.IndexOf (e.Document)));
			OnClosedHistoryChanged ();
			return Task.CompletedTask;
		}

		public void LogActiveDocument ()
		{
			LogActiveDocument (false);
		}

		public void Reset ()
		{
			history.Clear ();
			OnHistoryChanged ();
			closedHistory.Clear ();
			OnClosedHistoryChanged ();
		}

		public void LogActiveDocument (bool transient)
		{
			if (switching)
				return;

			var point = GetNavPointForActiveDoc (false);
			if (point == null)
				return;

			var item = new NavigationHistoryItem (point);

			//if the current node's transient but has been around for a while, consider making it permanent
			if (Current == null ||
				(currentIsTransient && DateTime.Now.Subtract (Current.Created).TotalMilliseconds > TRANSIENT_TIMEOUT)) {
				currentIsTransient = false;
			}

			//if the current point's transient, always replace it
			if (currentIsTransient) {
				//collapse down possible extra point in history
				var backOne = history [-1];
				if (backOne != null && point.ShouldReplace (backOne.NavigationPoint)) {
					// The new node is the same as the last permanent, so we can discard it
					history.RemoveCurrent ();
					currentIsTransient = false;
					item.Dispose ();
				} else {
					currentIsTransient = transient;
					history.ReplaceCurrent (item);
				}
			}
			//if the new point wants to replace the old one, let it
			else if (Current != null && !transient && point.ShouldReplace (Current.NavigationPoint)) {
				history.ReplaceCurrent (item);

				//but in this case, the point should not be transient -- unless the old point was,
				//but that's handled earlier
				currentIsTransient = false;
			}
			//final choice: append the the node
			//BUT only if the existing current node would not want to replace the new node
			else if (Current == null || !Current.NavigationPoint.ShouldReplace (point)) {
				history.AddPoint (item);
				currentIsTransient = transient;
			} else
				item.Dispose ();

			OnHistoryChanged ();
		}

		NavigationPoint GetNavPointForActiveDoc (bool forClosedHistory)
		{
			return GetNavPointForDoc (documentManager.ActiveDocument, forClosedHistory);
		}

		NavigationPoint GetNavPointForDoc (Document doc, bool forClosedHistory)
		{
			if (doc == null)
				return null;

			NavigationPoint point = null;

			INavigable navigable = doc.GetContent<INavigable> ();
			if (navigable != null) {
				point = navigable.BuildNavigationPoint ();
				if (point != null)
					return point;
			}

			var editBuf = doc.Editor;
			if (editBuf != null) {
				if (forClosedHistory) {
					point = new TextFileNavigationPoint (doc.FileName, editBuf.CaretLine, editBuf.CaretColumn);
				} else {
					point = new TextFileNavigationPoint (doc, editBuf);
				}
				if (point != null)
					return point;
			}

			return new DocumentNavigationPoint (doc);
		}

		#region Navigation

		public bool CanMoveForward {
			get { return history.CanMoveForward; }
		}

		public bool CanMoveBack {
			get { return history.CanMoveBack; }
		}

		public void MoveForward ()
		{
			LogActiveDocument ();
			if (history.CanMoveForward) {
				history.MoveForward ();
				SwitchToCurrent ();
				OnHistoryChanged ();
			}
		}

		public void MoveBack ()
		{
			// Log current point before moving back, to make sure a MoveForward will return to the same position
			LogActiveDocument ();
			if (history.CanMoveBack) {
				history.MoveBack ();
				SwitchToCurrent ();
				OnHistoryChanged ();
			}
		}

		public void MoveTo (NavigationHistoryItem item)
		{
			history.MoveTo (item);
			SwitchToCurrent ();
			OnHistoryChanged ();
		}

		void SwitchToCurrent ()
		{
			currentIsTransient = false;
			switching = true;
			if (history.Current != null)
				history.Current.Show ();
			switching = false;
		}

		#endregion

		#region Closed Document List

		public bool HasClosedDocuments {
			get { return closedHistory.Count != 0; }
		}

		public async void OpenLastClosedDocument ()
		{
			if (HasClosedDocuments) {
				int closedHistoryIndex = closedHistory.Count - 1;
				var tuple = closedHistory [closedHistoryIndex];
				closedHistory.RemoveAt (closedHistoryIndex);
				OnClosedHistoryChanged ();
				var doc = await tuple.Item1.ShowDocument ();
				if (doc != null)
					IdeApp.Workbench.ReorderTab (IdeApp.Workbench.Documents.IndexOf (doc), tuple.Item2);
			}
		}

		#endregion

		public IList<NavigationHistoryItem> GetNavigationList (int desiredLength)
		{
			return history.GetList (desiredLength);
		}

		public IList<NavigationHistoryItem> GetNavigationList (int desiredLength, out int currentIndex)
		{
			return history.GetList (desiredLength, out currentIndex);
		}

		public NavigationHistoryItem Current { get { return history.Current; } }

		public bool IsCurrent (NavigationHistoryItem point)
		{
			return history.IsCurrent (point);
		}

		public void Clear ()
		{
			history.Clear ();
			LogActiveDocument ();
		}

		public event EventHandler HistoryChanged;
		public event EventHandler ClosedHistoryChanged;

		void OnHistoryChanged ()
		{
			HistoryChanged?.Invoke (null, EventArgs.Empty);
		}

		void OnClosedHistoryChanged ()
		{
			ClosedHistoryChanged?.Invoke (null, EventArgs.Empty);
		}

		#region Handling active doc change events

		void ActiveDocChanged (object sender, EventArgs args)
		{
			LogActiveDocument (true);
			AttachToDoc (documentManager.ActiveDocument);
		}

		void AttachToDoc (Document document)
		{
			DetachFromCurrentDoc ();
			if (document == null)
				return;

			currentDoc = document;

			currentDoc.Closed += HandleCurrentDocClosed;

			if (currentDoc.Editor != null) {
				currentDoc.Editor.TextChanged += BufferTextChanged;
				currentDoc.Editor.CaretPositionChanged += BufferCaretPositionChanged;
			}
		}

		void HandleCurrentDocClosed (object sender, EventArgs e)
		{
			DetachFromCurrentDoc ();
		}

		void DetachFromCurrentDoc ()
		{
			if (currentDoc == null)
				return;

			currentDoc.Closed -= HandleCurrentDocClosed;
			if (currentDoc.Editor != null) {
				currentDoc.Editor.TextChanged -= BufferTextChanged;
				currentDoc.Editor.CaretPositionChanged -= BufferCaretPositionChanged;
			}
			currentDoc = null;
		}

		void BufferCaretPositionChanged (object sender, EventArgs args)
		{
			LogActiveDocument (true);
		}

		void BufferTextChanged (object sender, EventArgs args)
		{
			LogActiveDocument ();
		}

		#endregion

		#region Text file line number and snippet updating

		void LineCountChanged (object sender, LineCountEventArgs args)
		{
			//			MonoDevelop.Projects.Text.ITextFile textFile = (MonoDevelop.Projects.Text.ITextFile) sender;
		}

		void CommitCountChanges (object sender, TextFileEventArgs args)
		{
			//			MonoDevelop.Projects.Text.ITextFile textFile = (MonoDevelop.Projects.Text.ITextFile) sender;
		}

		void ResetCountChanges (object sender, TextFileEventArgs args)
		{
			//			MonoDevelop.Projects.Text.ITextFile textFile = (MonoDevelop.Projects.Text.ITextFile) sender;
		}

		void FileRenamed (object sender, ProjectFileRenamedEventArgs e)
		{
			bool historyChanged = false, closedHistoryChanged = false;

			foreach (var point in history) {
				foreach (var args in e) {
					var dp = point.NavigationPoint as DocumentNavigationPoint;
					historyChanged |= (dp?.HandleRenameEvent (args.OldName, args.NewName)).GetValueOrDefault ();
				}
			}

			if (historyChanged)
				OnHistoryChanged ();

			foreach (var point in closedHistory) {
				foreach (var args in e) {
					var dp = point.Item1 as DocumentNavigationPoint;
					closedHistoryChanged |= (dp?.HandleRenameEvent (args.OldName, args.NewName)).GetValueOrDefault ();
				}
			}

			if (closedHistoryChanged)
				OnClosedHistoryChanged ();
		}

		#endregion
	}
}
