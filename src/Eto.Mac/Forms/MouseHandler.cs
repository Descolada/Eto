namespace Eto.Mac.Forms
{
	public class MouseHandler : Mouse.IHandler
	{
		public void Initialize ()
		{
		}
		public Widget Widget { get; set; }

		public Eto.Platform Platform { get; set; }

		[DllImport(Constants.CoreGraphicsLibrary)]
 		static extern int CGWarpMouseCursorPosition(CGPoint point);

		[DllImport(Constants.CoreGraphicsLibrary)]
		static extern IntPtr CGEventCreate(IntPtr source);

		[DllImport(Constants.CoreGraphicsLibrary)]
		static extern CGPoint CGEventGetLocation(IntPtr handle);

		[DllImport(Constants.CoreFoundationLibrary)]
		static extern void CFRelease(IntPtr handle);

		public void SetCursor(Cursor cursor) => cursor.ToNS().Set();

		public Eto.Drawing.PointF Position
		{
			get
			{
				// Read the pointer through CoreGraphics rather than NSEvent.CurrentMouseLocation + NSScreen:
				// those touch AppKit (NSScreen in particular is main-thread-only) and raise an
				// AppKitThreadAccessException when Mouse.Position is queried off the main thread. CGEvent reports
				// the location in the same top-left, point-based global display space, and is thread-safe.
				var handle = CGEventCreate(IntPtr.Zero);
				if (handle == IntPtr.Zero)
					return Eto.Drawing.PointF.Empty;
				try
				{
					var location = CGEventGetLocation(handle);
					return new Eto.Drawing.PointF((float)location.X, (float)location.Y);
				}
				finally
				{
					CFRelease(handle);
				}
			}
			set
			{
				// CGWarpMouseCursorPosition already takes top-left, point-based global coordinates, which is what
				// PointF carries here — no NSScreen-based flip needed (and none was applied before).
				CGWarpMouseCursorPosition(value.ToNS());
			}
		}

		public MouseButtons Buttons
		{
			get
			{
				var current = NSEvent.CurrentPressedMouseButtons;
				var buttons = MouseButtons.None;
				if ((current & 1) != 0)
					buttons |= MouseButtons.Primary;
				if ((current & 2) != 0)
					buttons |= MouseButtons.Alternate;
				if ((current & 4) != 0)
					buttons |= MouseButtons.Middle;
				return buttons;
			}
		}
	}
}

