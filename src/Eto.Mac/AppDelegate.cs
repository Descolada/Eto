using System;
using Eto.Mac.Forms;



namespace Eto.Mac
{
	[Register("EtoAppDelegate")]
	public class AppDelegate : NSApplicationDelegate
	{
		/// <summary>
		/// Raised when macOS asks the application to open a file (Finder double-click, drag-to-Dock, `open` CLI, etc.).
		/// Registered with NSAppleEventManager in DidFinishLaunching for reliable delivery on all macOS versions.
		/// </summary>
		public static event Action<string> FileOpened;

		// Receives kAEOpenDocuments events directly from NSAppleEventManager.
		// On macOS 10.13+ the system prefers application:openURLs: (not bound by MonoMac) over the
		// application:openFile: delegate path, so we bypass the delegate chain entirely.
		[Register("EtoOpenDocumentHandler")]
		sealed class OpenDocumentHandler : NSObject
		{
			const uint KeyDirectObject = 0x2D2D2D2D; // '----'
			static readonly IntPtr s_selFileURLValue = Selector.GetHandle("fileURLValue");
			static readonly IntPtr s_selPath = Selector.GetHandle("path");

			[Export("handleOpenDocumentsEvent:withReplyEvent:")]
			public void HandleEvent(NSAppleEventDescriptor @event, NSAppleEventDescriptor reply)
			{
				try
				{
					var directParam = @event.ParamDescriptorForKeyword(KeyDirectObject);
					if (directParam == null) return;

					var count = directParam.NumberOfItems;
					if (count > 0)
					{
						for (int i = 1; i <= count; i++)
							NotifyPath(directParam.DescriptorAtIndex(i));
					}
					else
					{
						// directParam itself is the single-item descriptor (not wrapped in a list).
						NotifyPath(directParam);
					}
				}
				catch { }
			}

			static void NotifyPath(NSAppleEventDescriptor desc)
			{
				if (desc == null) return;

				// fileURLValue (macOS 10.11+) → NSURL* for typeFileURL descriptors.
				var urlHandle = Messaging.IntPtr_objc_msgSend(desc.Handle, s_selFileURLValue);
				if (urlHandle != IntPtr.Zero)
				{
					var pathHandle = Messaging.IntPtr_objc_msgSend(urlHandle, s_selPath);
					var path = NSString.FromHandle(pathHandle);
					if (!string.IsNullOrEmpty(path))
					{
						FileOpened?.Invoke(path);
						return;
					}
				}

				// Fallback: stringValue works for typeUnicodeText and some older descriptor types.
				var str = desc.StringValue;
				if (!string.IsNullOrEmpty(str))
				{
					try
					{
						var path = str.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
							? new Uri(str).LocalPath
							: str;
						if (!string.IsNullOrEmpty(path))
							FileOpened?.Invoke(path);
					}
					catch { }
				}
			}
		}

		// Static reference prevents the GC from collecting the handler between events.
		static OpenDocumentHandler s_openDocHandler;

		// Kept as secondary path for any macOS version that still routes through the delegate.
		public override bool OpenFile(NSApplication sender, string filename)
		{
			var fileOpened = FileOpened;
			if (fileOpened == null)
				return false;

			fileOpened(filename);
			return true;
		}

		public override void OpenFiles(NSApplication sender, string[] filenames)
		{
			var fileOpened = FileOpened;
			if (fileOpened == null)
			{
				sender.ReplyToOpenOrPrint(NSApplicationDelegateReply.Failure);
				return;
			}

			var reply = NSApplicationDelegateReply.Failure;
			try
			{
				foreach (var filename in filenames)
					fileOpened(filename);
				reply = NSApplicationDelegateReply.Success;
			}
			finally
			{
				sender.ReplyToOpenOrPrint(reply);
			}
		}

		public override bool ApplicationShouldHandleReopen(NSApplication sender, bool hasVisibleWindows)
		{
			if (!hasVisibleWindows)
			{
				var form = Application.Instance.MainForm;
				if (form != null)
					form.Show();
			}
			return true;
		}

		public override void DidBecomeActive(NSNotification notification)
		{
			var form = Application.Instance.MainForm;
			if (form != null && !form.Visible)
				form.Show();
		}

		public override void DidFinishLaunching(NSNotification notification)
		{
			var handler = Application.Instance.Handler as ApplicationHandler;
			if (handler != null)
			{
				handler.InitialMenu = Messaging.IntPtr_objc_msgSend(NSApplication.SharedApplication.Handle, MacWindow.selMainMenu);
				handler.Initialize(this);
			}

			// Override NSApplication's built-in kAEOpenDocuments handler so Finder file-opens reach
			// our FileOpened event regardless of which NSApplicationDelegate path macOS takes.
			// Registered here (after finishLaunching) so we overwrite NSApp's own registration.
			s_openDocHandler = new OpenDocumentHandler();
			NSAppleEventManager.SharedAppleEventManager.SetEventHandler(
				s_openDocHandler,
				new Selector("handleOpenDocumentsEvent:withReplyEvent:"),
				AEEventClass.AppleEvent,
				AEEventID.OpenDocuments);
		}

		public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender)
		{
			var args = new CancelEventArgs();
			var app = ((ApplicationHandler)Application.Instance.Handler);
			var form = Application.Instance.MainForm == null ? null : Application.Instance.MainForm.Handler as IMacWindow;
			if (form != null)
				args.Cancel = !form.CloseWindow(ce => app.Callback.OnTerminating(app.Widget, ce));
			else
			{
				app.Callback.OnTerminating(app.Widget, args);
			}
			return args.Cancel ? NSApplicationTerminateReply.Cancel : NSApplicationTerminateReply.Now;
		}
	}
}
