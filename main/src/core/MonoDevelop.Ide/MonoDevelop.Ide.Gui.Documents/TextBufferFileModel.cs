﻿//
// FileDocumentModel.cs
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
using MonoDevelop.Core;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Platform;
using Microsoft.VisualStudio.Utilities;
using MonoDevelop.Ide.Composition;
using MonoDevelop.Core.Text;
using System.Threading;
using System.Collections.Generic;
using System.Linq;

namespace MonoDevelop.Ide.Gui.Documents
{
	public class TextBufferFileModel : TextFileModel
	{
		public event EventHandler TextBufferInstanceChanged;

		public ITextBuffer TextBuffer => TextDocument.TextBuffer;

		public ITextDocument TextDocument => GetRepresentation<TextBufferFileModelRepresentation> ().TextDocument;

		protected virtual void OnTextBufferInstanceChanged ()
		{
			TextBufferInstanceChanged?.Invoke (this, EventArgs.Empty);
		}

		internal protected override Type RepresentationType => typeof (TextBufferFileModelRepresentation);

		protected internal override void OnRepresentationChanged ()
		{
			OnTextBufferInstanceChanged ();
		}

		class TextBufferFileModelRepresentation : TextFileModelRepresentation
		{
			ITextDocument textDocument;

			protected override async Task OnLoad ()
			{
				MimeType = (await Runtime.GetService<DesktopService> ()).GetMimeTypeForUri (FilePath);
				var contentType = (MimeType == null) ? PlatformCatalog.Instance.TextBufferFactoryService.InertContentType : GetContentTypeFromMimeType (FilePath, MimeType);

				var text = await TextFileUtility.GetTextAsync (FilePath, CancellationToken.None);
				var buffer = PlatformCatalog.Instance.TextBufferFactoryService.CreateTextBuffer (text.Text, contentType);
				var doc = PlatformCatalog.Instance.TextDocumentFactoryService.CreateTextDocument (buffer, FilePath);
				doc.Encoding = text.Encoding;
				Encoding = doc.Encoding;
				UseByteOrderMark = text.HasByteOrderMark;
				SetTextDocument (doc);
			}

			protected override void OnCreateNew ()
			{
				base.OnCreateNew ();
				SetTextDocument (CreateTextDocument (""));
				textDocument.Encoding = Encoding;
			}

			protected override string OnGetText ()
			{
				// OnLoad is always called before anything else, so the document should be ready
				return textDocument.TextBuffer.CurrentSnapshot.GetText ();
			}

			protected override void OnSetText (string text)
			{
				// OnLoad is always called before anything else, so the document should be ready
				var edit = textDocument.TextBuffer.CreateEdit ();
				edit.Replace (0, textDocument.TextBuffer.CurrentSnapshot.Length, text);
				edit.Apply ();
			}

			protected override Task OnSave ()
			{
				// OnLoad is always called before anything else, so the document should be ready
				textDocument.SaveAs (FilePath, true);
				return Task.CompletedTask;
			}

			public ITextDocument TextDocument => textDocument;

			ITextDocument CreateTextDocument (string text)
			{
				var contentType = (MimeType == null) ? PlatformCatalog.Instance.TextBufferFactoryService.InertContentType : GetContentTypeFromMimeType (FilePath, MimeType);
				var buffer = PlatformCatalog.Instance.TextBufferFactoryService.CreateTextBuffer (text, contentType);
				var doc = PlatformCatalog.Instance.TextDocumentFactoryService.CreateTextDocument (buffer, FilePath.ToString () ?? "");
				return doc;
			}

			protected static IContentType GetContentTypeFromMimeType (string filePath, string mimeType)
			{
				if (filePath != null) {
					var fileToContentTypeService = CompositionManager.Instance.GetExportedValue<IFileToContentTypeService> ();
					var contentTypeFromPath = fileToContentTypeService.GetContentTypeForFilePath (filePath);
					if (contentTypeFromPath != null &&
						contentTypeFromPath != PlatformCatalog.Instance.ContentTypeRegistryService.UnknownContentType) {
						return contentTypeFromPath;
					}
				}
				var contentType = PlatformCatalog.Instance.MimeToContentTypeRegistryService.GetContentType (mimeType);
				if (contentType == null) {
					// fallback 1: see if there is a content tyhpe with the same name
					contentType = PlatformCatalog.Instance.ContentTypeRegistryService.GetContentType (mimeType);
					if (contentType == null) {
						// No joy, create a content type that, by default, derives from text. This is strictly an error
						// (there should be mappings between any mime type and any content type).
						contentType = PlatformCatalog.Instance.ContentTypeRegistryService.AddContentType (mimeType, new string [] { "text" });
					}
				}

				return contentType;
			}

			void SetTextDocument (ITextDocument doc)
			{
				if (doc != textDocument) {
					if (textDocument != null) {
						textDocument.TextBuffer.Changed -= TextBuffer_Changed;
						textDocument.DirtyStateChanged -= TextDocument_DirtyStateChanged;
					}
					textDocument = doc;
					if (textDocument != null) {
						textDocument.TextBuffer.Changed += TextBuffer_Changed;
						textDocument.DirtyStateChanged += TextDocument_DirtyStateChanged;
					}
				}
			}

			void TextBuffer_Changed (object sender, TextContentChangedEventArgs e)
			{
				NotifyChanged ();
			}

			void TextDocument_DirtyStateChanged (object sender, EventArgs e)
			{
				HasUnsavedChanges = textDocument.IsDirty;
			}

			protected internal override Task OnDispose ()
			{
				textDocument?.Dispose ();
				return base.OnDispose ();
			}
		}
	}
}
