namespace Eto.Forms;

/// <summary>
/// Result codes for <see cref="CommonDialog"/> or <see cref="MessageBox"/> dialogs
/// </summary>
/// <copyright>(c) 2014 by Curtis Wensley</copyright>
/// <license type="BSD-3">See LICENSE for full terms</license>
public enum DialogResult
{
	/// <summary>
	/// No specific result
	/// </summary>
	None,
	/// <summary>
	/// User clicked 'OK'
	/// </summary>
	Ok,
	/// <summary>
	/// User clicked 'Cancel' or pressed escape to cancel
	/// </summary>
	Cancel,
	/// <summary>
	/// User clicked 'Yes'
	/// </summary>
	Yes,
	/// <summary>
	/// User clicked 'No'
	/// </summary>
	No,
	/// <summary>
	/// User clicked 'Abort'
	/// </summary>
	Abort,
	/// <summary>
	/// User clicked 'Ignore'
	/// </summary>
	Ignore,
	/// <summary>
	/// User clicked 'Retry'
	/// </summary>
	Retry
}

/// <summary>
/// Base class for common dialogs
/// </summary>
public abstract class CommonDialog : Widget
{
	new IHandler Handler { get { return (IHandler)base.Handler; } }

	/// <summary>
	/// Initializes a new instance of the <see cref="Eto.Forms.CommonDialog"/> class.
	/// </summary>
	protected CommonDialog()
	{
	}

	/// <summary>
	/// Shows the dialog with the specified parent, blocking until a result is returned.
	/// </summary>
	/// <returns>The dialog result.</returns>
	/// <param name="parent">Parent control</param>
	public DialogResult ShowDialog(Control parent)
	{
		return ShowDialog(parent != null ? parent.ParentWindow : null);
	}

	/// <summary>
	/// Shows the dialog with the specified parent window, blocking until a result is returned.
	/// </summary>
	/// <returns>The dialog result.</returns>
	/// <param name="parent">Parent window.</param>
	public virtual DialogResult ShowDialog(Window parent)
	{
		return Handler.ShowDialog(parent);
	}

	/// <summary>
	/// Shows the dialog asynchronously with the specified parent.
	/// </summary>
	public Task<DialogResult> ShowDialogAsync(Control parent, CancellationToken cancellationToken = default)
	{
		return ShowDialogAsync(parent != null ? parent.ParentWindow : null, cancellationToken);
	}

	/// <summary>
	/// Shows the dialog asynchronously with the specified parent window.
	/// </summary>
	public virtual Task<DialogResult> ShowDialogAsync(Window parent, CancellationToken cancellationToken = default)
	{
		Application.Instance.EnsureUIThread();

		if (cancellationToken.IsCancellationRequested)
			return Task.FromCanceled<DialogResult>(cancellationToken);

		if (Handler is not ICancellableHandler cancellableHandler)
			throw new NotSupportedException($"{GetType().Name} does not support asynchronous display.");

		var tcs = new TaskCompletionSource<DialogResult>();
		var cancellationRequested = 0;
		var registration = cancellationToken.Register(() =>
		{
			Interlocked.Exchange(ref cancellationRequested, 1);
			Application.Instance.AsyncInvoke(cancellableHandler.CancelDialog);
		});

		Application.Instance.AsyncInvoke(() =>
		{
			try
			{
				if (Volatile.Read(ref cancellationRequested) != 0)
				{
					tcs.TrySetCanceled();
					return;
				}

				var result = Handler.ShowDialog(parent);

				if (Volatile.Read(ref cancellationRequested) != 0)
					tcs.TrySetCanceled();
				else
					tcs.TrySetResult(result);
			}
			catch (Exception ex)
			{
				tcs.TrySetException(ex);
			}
			finally
			{
				registration.Dispose();
			}
		});

		return tcs.Task;
	}

	/// <summary>
	/// Handler interface for the <see cref="CommonDialog"/>
	/// </summary>
	public new interface IHandler : Widget.IHandler
	{
		/// <summary>
		/// Shows the dialog with the specified parent window, blocking until a result is returned.
		/// </summary>
		/// <returns>The dialog result.</returns>
		/// <param name="parent">Parent window.</param>
		DialogResult ShowDialog(Window parent);
	}

	/// <summary>
	/// Handler interface for common dialogs which can terminate an active modal display.
	/// </summary>
	public interface ICancellableHandler : IHandler
	{
		/// <summary>
		/// Cancels the active dialog.
		/// </summary>
		void CancelDialog();
	}
}
