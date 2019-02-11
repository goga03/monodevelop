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
using System.Threading.Tasks;
using MonoDevelop.Components;
using MonoDevelop.Ide.Gui.Shell;
using System.Threading;
using MonoDevelop.Ide.Gui.Content;

namespace MonoDevelop.Ide.Gui.Documents
{
	/// <summary>
	/// A document view that shows the content in a control
	/// </summary>
	public sealed class DocumentViewContent : DocumentView
	{
		private Func<CancellationToken,Task<Control>> asyncContentLoader;
		private Func<Control> contentLoader;
		IShellDocumentViewContent shellContentView;
		DocumentToolbar toolbar;
		IPathedDocument pathDoc;
		Control control;

		public DocumentViewContent (Func<CancellationToken,Task<Control>> contentLoader)
		{
			this.asyncContentLoader = contentLoader;
			ActiveViewInHierarchy = this;
		}

		public DocumentViewContent (Func<Control> contentLoader)
		{
			ContentLoader = contentLoader;
			ActiveViewInHierarchy = this;
		}

		internal override IShellDocumentViewItem OnCreateShellView (IWorkbenchWindow window)
		{
			shellContentView = window.CreateViewContent ();
			shellContentView.SetContentLoader (AsyncContentLoader);
			if (pathDoc != null)
				shellContentView.ShowPathBar (pathDoc);
			return shellContentView;
		}

		void AttachToView (IShellDocumentViewItem shellView)
		{
		}

		/// <summary>
		/// Async callback to be invoked to retrieve the control that the view will display
		/// </summary>
		public Func<CancellationToken,Task<Control>> AsyncContentLoader {
			get => asyncContentLoader;
			set {
				if (asyncContentLoader != value) {
					asyncContentLoader = value;
					if (shellContentView != null) {
						var oldControl = control;
						control = null;
						shellContentView.SetContentLoader (asyncContentLoader);
						if (oldControl != null) {
							if (control == null)
								shellContentView.ReloadContent (); // Make sure the shell view uses the new control
							oldControl.Dispose ();
						}
					}
				}
			}
		}

		/// <summary>
		/// Callback to be invoked to retrieve the control that the view will display
		/// </summary>
		public Func<Control> ContentLoader {
			get => contentLoader;
			set {
				if (contentLoader != value) {
					contentLoader = value;
					AsyncContentLoader = delegate (CancellationToken ct) {
						return Task.FromResult<Control> (contentLoader ());
					};
				}
			}
		}

		public DocumentToolbar GetToolbar ()
		{
			if (shellContentView == null)
				throw new InvalidOperationException ("Toolbar can't be requested before the view content is created");
			if (toolbar == null)
				toolbar = new DocumentToolbar (shellContentView.GetToolbar ());
			return toolbar;
		}

		public void ShowPathBar (IPathedDocument pathDoc)
		{
			if (shellContentView == null)
				this.pathDoc = pathDoc;
			else
				shellContentView.ShowPathBar (pathDoc);
		}

		public void HidePathBar ()
		{
			if (shellContentView == null)
				this.pathDoc = null;
			else
				shellContentView.HidePathBar ();
		}

		protected override void OnDispose ()
		{
			base.OnDispose ();
			if (control != null)
				control.Dispose ();
		}

		internal override void OnActivated ()
		{
			base.OnActivated ();
			if (Parent != null)
				Parent.ActiveViewInHierarchy = this;
		}
	}
}
