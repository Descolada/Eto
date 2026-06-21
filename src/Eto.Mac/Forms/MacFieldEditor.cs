using Eto.Mac.Forms.Controls;
using Eto.Mac.Forms.Menu;


namespace Eto.Mac.Forms
{
	public class MacFieldEditor : NSTextView, IMacControl
	{
		public MacFieldEditor()
		{
			FieldEditor = true;
		}

		public MacFieldEditor(IntPtr handle)
			: base(handle)
		{
		}

		public IMacControl MacControl => WeakDelegate as IMacControl;
		public object Handler => MacControl?.WeakHandler?.Target;

		public IMacViewHandler MacViewHandler => Handler as IMacViewHandler;

		WeakReference IMacControl.WeakHandler
		{
			get => MacControl?.WeakHandler;
			set { }
		}

		// Opt-in property key. An Eto control that sets this to true on its Properties will
		// receive command-key combinations (e.g. Cmd+A) as KeyDown events via the field editor
		// before the main menu can consume them as key equivalents (Select All, Copy, etc.).
		// macOS dispatches view-level performKeyEquivalent: ahead of the menu, so this lets a
		// control such as a hotkey-capture box claim those combos. It is opt-in to avoid firing
		// duplicate KeyDown events on ordinary text fields for unclaimed command combinations.
		public const string CaptureKeyEquivalentsProperty = "Eto.Mac.MacFieldEditor.CaptureKeyEquivalents";

		public override bool PerformKeyEquivalent(NSEvent theEvent)
		{
			var handler = Handler as IMacViewHandler;
			if (handler?.Widget != null
				&& handler.Widget.Properties.Get<bool>(CaptureKeyEquivalentsProperty)
				&& MacEventView.KeyDown(handler.Widget, theEvent))
				return true;

			return base.PerformKeyEquivalent(theEvent);
		}

		public override void KeyDown(NSEvent theEvent)
		{
			var handler = Handler as IMacViewHandler;
			if (handler != null && MacEventView.KeyDown(handler.Widget, theEvent))
				return;

			base.KeyDown(theEvent);
		}

		public override void FlagsChanged(NSEvent theEvent)
		{
			var handler = Handler as IMacViewHandler;
			if (handler != null && MacEventView.FlagsChanged(handler.Widget, theEvent))
				return;

			base.FlagsChanged(theEvent);
		}

		void MouseDownEvent(NSEvent theEvent, Action<NSEvent> baseMethod)
		{
			var handler = Handler as IMacViewHandler;
			if (handler == null)
			{
				baseMethod(theEvent);
				return;
			}

			if (handler.Widget?.IsDisposed != false) return;

			if (handler.SuppressMouseEvents > 0)
			{
				// we can get called from a MouseDown from the owning object
				baseMethod(theEvent);
				return;
			}

			var args = MacConversions.GetMouseEvent(handler, theEvent, false);
			if (theEvent.ClickCount >= 2)
				handler.Callback.OnMouseDoubleClick(handler.Widget, args);

			if (!args.Handled)
			{
				handler.Callback.OnMouseDown(handler.Widget, args);
			}

			if (!args.Handled)
			{
				baseMethod(theEvent);

				// trigger mouse up here, if needed				
				handler.TriggerMouseCallback();
			}
		}

		bool MouseUpEvent(NSEvent theEvent)
		{
			var handler = Handler as IMacViewHandler;
			if (handler == null)
				return false;

			if (handler.Widget?.IsDisposed != false) return false;

			var args = MacConversions.GetMouseEvent(handler, theEvent, false);
			handler.Callback.OnMouseUp(handler.Widget, args);
			return args.Handled;
		}

		public override void MouseDown(NSEvent theEvent)
		{
			MouseDownEvent(theEvent, base.MouseDown);
		}

		public override void MouseUp(NSEvent theEvent)
		{
			if (!MouseUpEvent(theEvent))
				base.MouseUp(theEvent);
		}

		public override void RightMouseDown(NSEvent theEvent)
		{
			MouseDownEvent(theEvent, base.RightMouseDown);
		}

		public override void RightMouseUp(NSEvent theEvent)
		{
			if (!MouseUpEvent(theEvent))
				base.RightMouseUp(theEvent);
		}

		public override void OtherMouseDown(NSEvent theEvent)
		{
			MouseDownEvent(theEvent, base.OtherMouseDown);
		}

		public override void OtherMouseUp(NSEvent theEvent)
		{
			if (!MouseUpEvent(theEvent))
				base.OtherMouseUp(theEvent);
		}

		public override bool ShouldChangeText(NSRange affectedCharRange, string replacementString)
		{
			var handler = Handler as IMacTextBoxHandler;
			if (handler != null)
			{
				var args = new TextChangingEventArgs(replacementString, affectedCharRange.ToEto(), true);
				handler.Callback.OnTextChanging(handler.Widget, args);
				if (args.Cancel)
					return false;
			}

			return base.ShouldChangeText(affectedCharRange, replacementString);
		}

		[Export("menuForEvent:")]
		public NSMenu OnMenuForEvent(NSEvent theEvent)
		{
			var handler = MacViewHandler;
			if (handler != null)
			{
				var nativeMenu = (handler.Widget.ContextMenu?.Handler as ContextMenuHandler)?.Control;
				return nativeMenu;
			}
			return null;
		}


		public override bool ResignFirstResponder()
		{
			var handler = Handler as IMacViewHandler;
			if (handler != null && handler.ShouldHaveFocus == null)
			{
				handler.ShouldHaveFocus = false;
				// for some reason calling base.ResignFirstResponder calls this method again???
				var result = base.ResignFirstResponder();
				handler.Callback.OnLostFocus(handler.Widget, EventArgs.Empty);
				handler.ShouldHaveFocus = null;
				return result;
			}
			return base.ResignFirstResponder();
		}

		public override void InsertText(NSObject text, NSRange replacementRange)
		{
			var handler = Handler as IMacViewHandler;
			if (handler != null && text is NSString str)
			{
				var args = new TextInputEventArgs(str);
				handler.Callback.OnTextInput(handler.Widget, args);
				if (args.Cancel)
					return;
			}
			base.InsertText(text, replacementRange);
		}
	}
}
