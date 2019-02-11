﻿//
// DocumentViewContainer.cs
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
using MonoDevelop.Components.Commands;
using MonoDevelop.Ide.Gui.Shell;
using System.Linq;

namespace MonoDevelop.Ide.Gui.Documents
{
	/// <summary>
	/// Base type for views that can show the content of documents
	/// </summary>
	public abstract class DocumentView : ICommandDelegator, IDocumentViewContentCollectionListener, IDisposable
	{
		string title;
		string accessibilityDescription;
		Xwt.Drawing.Image icon;
		DocumentViewContent activeChildView;
		IShellDocumentViewItem shellView;
		IShellDocumentViewContainer attachmentsContainer;
		IShellDocumentViewItem mainShellView;
		DocumentView activeAttachedView;
		internal IWorkbenchWindow window;

		/// <summary>
		/// Raised when the active view in this hierarchy of views changes
		/// </summary>
		public EventHandler ActiveViewInHierarchyChanged;
		private DocumentController sourceController;

		public DocumentView ()
		{
			AttachedViews.AttachListener (this);
		}

		/// <summary>
		/// The controller that was used to create this view
		/// </summary>
		/// <value>The parent controller.</value>
		public DocumentController SourceController {
			get { return sourceController; }
			internal set {
				UnsubscribeControllerEvents ();
				sourceController = value;
				SubscribeControllerEvents ();
			}
		}

		public DocumentViewContentCollection AttachedViews { get; } = new DocumentViewContentCollection ();

		/// <summary>
		/// Title of the view. This is the text shown in tabs and selectors.
		/// </summary>
		public string Title {
			get => title ?? SourceController?.TabPageLabel;
			set {
				title = value;
				UpdateTitle ();
			}
		}

		public string AccessibilityDescription {
			get { return accessibilityDescription ?? SourceController?.AccessibilityDescription; }
			set { accessibilityDescription = value; UpdateTitle (); }
		}

		public Xwt.Drawing.Image Icon {
			get => icon;
			set {
				icon = value;
				UpdateTitle ();
			}
		}

		public DocumentViewContent ActiveViewInHierarchy {
			get => activeChildView;
			set {
				if (value != activeChildView) {
					activeChildView = value;
					ActiveViewInHierarchyChanged?.Invoke (this, EventArgs.Empty);
					if (Parent != null) {
						if (Parent is DocumentViewContainer container) {
							if (container.ActiveView == this)
								Parent.ActiveViewInHierarchy = value;
						} else
							Parent.ActiveViewInHierarchy = value; // Must be an attached view
					}
				}
			}
		}

		internal void UpdateActiveViewInHierarchy ()
		{
		}

		internal virtual void OnActivated ()
		{
		}

		/// <summary>
		/// Container that contains this view
		/// </summary>
		/// <value>The parent.</value>
		public DocumentView Parent { get; internal set; }

		internal IShellDocumentViewItem ShellView {
			get {
				return shellView;
			}
		}

		object ICommandDelegator.GetDelegatedCommandTarget () => SourceController;

		public void SetActive ()
		{
			if (AttachedViews.Count > 0 && activeAttachedView != this) {
				if (attachmentsContainer != null)
					attachmentsContainer.ActiveView = mainShellView;
				else {
					activeAttachedView = this;
					activeAttachedView.OnActivated ();
				}
				return;
			}
			if (Parent != null)
				Parent.SetActiveChild (this);
		}

		public void BringToFront ()
		{
			SetActive ();
			if (Parent is DocumentViewContainer)
				Parent.SetActive ();
		}

		public void Dispose ()
		{
			OnDispose ();
		}

		internal virtual void SetActiveChild (DocumentView child)
		{
			if (AttachedViews.Contains (child)) {
				if (attachmentsContainer != null) {
					attachmentsContainer.ActiveView = child.ShellView;
				} else {
					activeAttachedView = child;
					activeAttachedView.OnActivated ();
				}
			}
		}

		void UpdateTitle ()
		{
			if (shellView != null)
				shellView.SetTitle (Title, icon, accessibilityDescription);
			if (mainShellView != shellView && mainShellView != null)
				mainShellView.SetTitle (Title, icon, accessibilityDescription);
		}

		internal bool IsRoot { get; set; }

		internal IShellDocumentViewItem CreateShellView (IWorkbenchWindow window)
		{
			if (shellView != null)
				return shellView;
			this.window = window;
			mainShellView = OnCreateShellView (window);
			if (IsRoot && AttachedViews.Count > 0) {
				attachmentsContainer = window.CreateViewContainer ();
				attachmentsContainer.SetSupportedModes (DocumentViewContainerMode.Tabs);
				attachmentsContainer.SetCurrentMode (DocumentViewContainerMode.Tabs);
				attachmentsContainer.InsertView (0, mainShellView);
				int pos = 1;
				foreach (var attachedView in AttachedViews)
					attachmentsContainer.InsertView (pos++, attachedView.CreateShellView (window));
				attachmentsContainer.ActiveViewChanged += AttachmentsContainer_ActiveViewChanged;
				shellView = attachmentsContainer;
			} else
				shellView = mainShellView;

			UpdateTitle ();
			shellView.SetDelegatedCommandTarget (this);
			return shellView;
		}

		internal void ReplaceViewInParent ()
		{
			if (Parent == null) {
				if (IsRoot)
					window.SetRootView (shellView);
			} else {
				Parent.ReplaceChildView (this);
			}
		}

		internal virtual bool ReplaceChildView (DocumentView documentView)
		{
			var index = AttachedViews.IndexOf (documentView);
			if (index == -1)
				return false;
			if (attachmentsContainer != null)
				attachmentsContainer.ReplaceView (index, documentView.CreateShellView (window));
			return true;
		}

		internal virtual bool RemoveChild (DocumentView view)
		{
			return AttachedViews.Remove (view);
		}

		void UpdateAttachmentsContainer ()
		{
			if (!IsRoot)
				return;
			if (AttachedViews.Count > 0) {
				if (attachmentsContainer == null && window != null) {
					// Attachments notebook needs to be added
					attachmentsContainer = window.CreateViewContainer ();
					attachmentsContainer.InsertView (0, mainShellView);
					int pos = 1;
					foreach (var attachedView in AttachedViews)
						attachmentsContainer.InsertView (pos++, attachedView.CreateShellView (window));
					shellView = attachmentsContainer;
					attachmentsContainer.ActiveViewChanged += AttachmentsContainer_ActiveViewChanged;
					UpdateTitle ();
					ReplaceViewInParent ();
				}
			} else {
				if (attachmentsContainer != null) {
					// No more attachments, the attachments notebook can be removed
					attachmentsContainer.RemoveView (0);
					attachmentsContainer.ActiveViewChanged -= AttachmentsContainer_ActiveViewChanged;
					attachmentsContainer.Dispose ();
					attachmentsContainer = null;
					shellView = mainShellView;
					ReplaceViewInParent ();
				}
			}
		}

		void AttachmentsContainer_ActiveViewChanged (object sender, EventArgs e)
		{
			activeAttachedView = attachmentsContainer.ActiveView?.Item;
			activeAttachedView?.OnActivated ();
		}

		internal abstract IShellDocumentViewItem OnCreateShellView (IWorkbenchWindow window);

		internal virtual void DetachFromView ()
		{
			IsRoot = false;
			window = null;
			shellView = null;
		}

		protected virtual void OnDispose ()
		{
			if (Parent != null)
				Parent.RemoveChild (this);
			else if (IsRoot)
				throw new InvalidOperationException ("Can't dispose the root view of a document");
			if (shellView != null)
				shellView.Dispose ();
		}

		internal virtual IEnumerable<DocumentController> GetActiveControllerHierarchy ()
		{
			if (SourceController != null)
				yield return SourceController;
		}

		internal virtual IEnumerable<DocumentController> GetAllControllers ()
		{
			var result = AttachedViews.SelectMany (v => v.GetAllControllers ());
			if (SourceController != null)
				result = result.Concat (SourceController);
			return result;
		}

		void SubscribeControllerEvents ()
		{
			if (SourceController != null) {
				SourceController.AccessibilityDescriptionChanged += SourceController_AccessibilityDescriptionChanged;
				SourceController.DocumentTitleChanged += SourceController_AccessibilityDescriptionChanged;
			}
		}

		void UnsubscribeControllerEvents ()
		{
			if (SourceController != null) {
				SourceController.AccessibilityDescriptionChanged -= SourceController_AccessibilityDescriptionChanged;
				SourceController.DocumentTitleChanged -= SourceController_AccessibilityDescriptionChanged;
			}
		}

		void SourceController_AccessibilityDescriptionChanged (object sender, EventArgs e)
		{
			UpdateTitle ();
		}

		void IDocumentViewContentCollectionListener.ClearItems (DocumentViewContentCollection list)
		{
			OnClearItems (list);
		}

		void IDocumentViewContentCollectionListener.InsertItem (DocumentViewContentCollection list, int index, DocumentView item)
		{
			OnInsertItem (list, index, item);
		}

		void IDocumentViewContentCollectionListener.RemoveItem (DocumentViewContentCollection list, int index)
		{
			OnRemoveItem (list, index);
		}

		void IDocumentViewContentCollectionListener.SetItem (DocumentViewContentCollection list, int index, DocumentView item)
		{
			OnSetItem (list, index, item);
		}

		void IDocumentViewContentCollectionListener.ItemsCleared (DocumentViewContentCollection list)
		{
			OnItemsCleared (list);
		}

		void IDocumentViewContentCollectionListener.ItemInserted (DocumentViewContentCollection list, int index, DocumentView item)
		{
			OnItemInserted (list, index, item);
		}

		void IDocumentViewContentCollectionListener.ItemRemoved (DocumentViewContentCollection list, int index)
		{
			OnItemRemoved (list, index);
		}

		void IDocumentViewContentCollectionListener.ItemSet (DocumentViewContentCollection list, int index, DocumentView item)
		{
			OnItemSet (list, index, item);
		}

		internal virtual void OnClearItems (DocumentViewContentCollection list)
		{
			if (list == AttachedViews) {
				foreach (var it in AttachedViews)
					it.Parent = null;
				if (attachmentsContainer != null)
					attachmentsContainer.RemoveAllViews ();
			}
		}

		internal virtual void OnInsertItem (DocumentViewContentCollection list, int index, DocumentView item)
		{
			if (list == AttachedViews) {
				item.Parent = this;
				if (attachmentsContainer != null)
					attachmentsContainer.InsertView (index, item.CreateShellView (window));
			}
		}

		internal virtual void OnRemoveItem (DocumentViewContentCollection list, int index)
		{
			if (list == AttachedViews) {
				AttachedViews [index].Parent = null;
				if (attachmentsContainer != null)
					attachmentsContainer.RemoveView (index);
			}
		}

		internal virtual void OnSetItem (DocumentViewContentCollection list, int index, DocumentView item)
		{
			if (list == AttachedViews) {
				AttachedViews [index].Parent = null;
				item.Parent = this;
				if (attachmentsContainer != null)
					attachmentsContainer.ReplaceView (index, item.CreateShellView (window));
			}
		}

		internal virtual void OnItemsCleared (DocumentViewContentCollection list)
		{
			if (list == AttachedViews)
				UpdateAttachmentsContainer ();
		}

		internal virtual void OnItemInserted (DocumentViewContentCollection list, int index, DocumentView item)
		{
			if (list == AttachedViews)
				UpdateAttachmentsContainer ();
		}

		internal virtual void OnItemRemoved (DocumentViewContentCollection list, int index)
		{
			if (list == AttachedViews) {
				UpdateAttachmentsContainer ();
			}
		}

		internal virtual void OnItemSet (DocumentViewContentCollection list, int index, DocumentView item)
		{
		}

		public override string ToString ()
		{
			return GetType ().Name + " - " + Title;
		}
	}
}
