namespace Eto.WinForms.Forms
{
	public class MessageBoxHandler : WidgetHandler<Widget>, MessageBox.IHandler
	{
		public string Text { get; set; }

		public string Caption { get; set; }

		public MessageBoxType Type { get; set; }

		public MessageBoxButtons Buttons { get; set; }

		public MessageBoxDefaultButton DefaultButton { get; set; }

		public DialogResult ShowDialog(Control parent)
		{
			var parentWindow = parent?.ParentWindow;
			if (parentWindow?.HasFocus == false)
				parentWindow.Focus();

			var caption = Caption ?? parentWindow?.Title;
			swf.Control c = (parent == null) ? null : (swf.Control)parent.ControlObject;
			swf.DialogResult result = swf.MessageBox.Show(c, Text, caption, Convert(Buttons), Convert(Type), Convert(DefaultButton, Buttons));
			return result.ToEto();
		}

		public Task<DialogResult> ShowDialogAsync(Control parent, CancellationToken cancellationToken = default)
		{
			var tcs = new TaskCompletionSource<DialogResult>();

			Application.Instance.InvokeAsync(() =>
			{
				CancellationTokenRegistration ctr = default;
				swf.Form cancelOwner = null;
				try
				{
					var parentWindow = parent?.ParentWindow;
					if (parentWindow?.HasFocus == false)
						parentWindow.Focus();

					var caption = Caption ?? parentWindow?.Title;
					swf.Control ownerControl;

					bool useCancelOwner = cancellationToken.CanBeCanceled || cancellationToken.IsCancellationRequested;
					if (useCancelOwner)
					{
						if (cancellationToken.IsCancellationRequested)
						{
							tcs.TrySetCanceled();
							ctr.Dispose();
							return;
						}
						// Create a hidden owner to own the message box, so we can close it later if cancelled
						cancelOwner = new swf.Form
						{
							Size = sd.Size.Empty,
						};
						if (parentWindow?.ControlObject is swf.Form parentForm)
							cancelOwner.Owner = parentForm;

						ownerControl = cancelOwner;

						ctr = cancellationToken.Register(() =>
						{
							if (cancelOwner != null && !cancelOwner.IsDisposed)
							{
								cancelOwner.BeginInvoke(new Action(() =>
								{
									if (tcs.TrySetCanceled())
										CloseMessageBox(cancelOwner); // Just disposing of the owner causes a flicker (unlike WPF), so close the message box properly
								}));
							}
							else
								_ = tcs.TrySetCanceled();
						});
					}
					else
					{
						ownerControl = parent == null ? null : (swf.Control)parent.ControlObject;
					}

					var result = swf.MessageBox.Show(ownerControl, Text, caption, Convert(Buttons), Convert(Type), Convert(DefaultButton, Buttons));
					tcs.TrySetResult(result.ToEto());
				}
				catch (Exception ex)
				{
					tcs.TrySetException(ex);
				}
				finally
				{
					ctr.Dispose();
					cancelOwner?.Owner?.Focus();
					cancelOwner?.Dispose();
				}
			});

			return tcs.Task;
		}

		static void CloseMessageBox(swf.Form owner)
		{
			if (owner == null || owner.IsDisposed || !owner.IsHandleCreated)
				return;

			var ownerHandle = owner.Handle;
			IntPtr messageBoxHandle = IntPtr.Zero;
			var threadId = Win32.GetCurrentThreadId();
			Win32.EnumThreadProc callback = (hWnd, lParam) =>
			{
				if (hWnd == ownerHandle)
					return true;

				if (Win32.GetWindow(hWnd, Win32.GW.OWNER) != ownerHandle)
					return true;

				if (!Win32.IsDialogWindow(hWnd))
					return true;

				messageBoxHandle = hWnd;
				return false;
			};

			Win32.EnumThreadWindows(threadId, callback, IntPtr.Zero);

			if (messageBoxHandle != IntPtr.Zero)
				Win32.PostMessage(messageBoxHandle, Win32.WM.CLOSE, IntPtr.Zero, IntPtr.Zero);
		}

		public static swf.MessageBoxDefaultButton Convert(MessageBoxDefaultButton defaultButton, MessageBoxButtons buttons)
		{
			switch (defaultButton)
			{
				case MessageBoxDefaultButton.OK:
					return swf.MessageBoxDefaultButton.Button1;
				case MessageBoxDefaultButton.No:
				case MessageBoxDefaultButton.Cancel:
					return swf.MessageBoxDefaultButton.Button2;
				case MessageBoxDefaultButton.Default:
					switch (buttons)
					{
						case MessageBoxButtons.OK:
							return swf.MessageBoxDefaultButton.Button1;
						case MessageBoxButtons.YesNo:
						case MessageBoxButtons.OKCancel:
							return swf.MessageBoxDefaultButton.Button2;
						case MessageBoxButtons.YesNoCancel:
							return swf.MessageBoxDefaultButton.Button3;
						default:
							throw new NotSupportedException();
					}
				default:
					throw new NotSupportedException();
			}
		}

		public static swf.MessageBoxButtons Convert(MessageBoxButtons buttons)
		{
			switch (buttons)
			{
				case MessageBoxButtons.OK: return swf.MessageBoxButtons.OK;
				case MessageBoxButtons.OKCancel: return swf.MessageBoxButtons.OKCancel;
				case MessageBoxButtons.YesNo: return swf.MessageBoxButtons.YesNo;
				case MessageBoxButtons.YesNoCancel: return swf.MessageBoxButtons.YesNoCancel;
				default:
					throw new NotSupportedException();
			}
		}

		public static swf.MessageBoxIcon Convert(MessageBoxType type)
		{
			switch (type)
			{
				case MessageBoxType.Error: return swf.MessageBoxIcon.Error;
				case MessageBoxType.Warning: return swf.MessageBoxIcon.Warning;
				case MessageBoxType.Information: return swf.MessageBoxIcon.Information;
				case MessageBoxType.Question: return swf.MessageBoxIcon.Question;
				default:
					throw new NotSupportedException();
			}
		}
	}
}
