using Eto.Mac.Drawing;

namespace Eto.Mac.Forms
{
	public class ScreenHandler : WidgetHandler<NSScreen, Screen>, Screen.IHandler
	{
		const string ScreenCaptureKitPath = "/System/Library/Frameworks/ScreenCaptureKit.framework/ScreenCaptureKit";
		static readonly IntPtr ScreenCaptureKitHandle = Dlfcn.dlopen(ScreenCaptureKitPath, 0);
		static readonly IntPtr ScreenshotManagerClass = Class.GetHandle("SCScreenshotManager");
		static readonly IntPtr ShareableContentClass = Class.GetHandle("SCShareableContent");
		static readonly IntPtr ContentFilterClass = Class.GetHandle("SCContentFilter");
		static readonly IntPtr StreamConfigurationClass = Class.GetHandle("SCStreamConfiguration");
		static readonly IntPtr CaptureImageInRectSelector = Selector.GetHandle("captureImageInRect:completionHandler:");
		static readonly IntPtr CaptureImageWithFilterSelector = Selector.GetHandle("captureImageWithFilter:configuration:completionHandler:");
		static readonly IntPtr GetShareableContentSelector = Selector.GetHandle("getShareableContentWithCompletionHandler:");
		static readonly IntPtr DisplaysSelector = Selector.GetHandle("displays");
		static readonly IntPtr DisplayIdSelector = Selector.GetHandle("displayID");
		static readonly IntPtr CountSelector = Selector.GetHandle("count");
		static readonly IntPtr ObjectAtIndexSelector = Selector.GetHandle("objectAtIndex:");
		static readonly IntPtr AllocSelector = Selector.GetHandle("alloc");
		static readonly IntPtr InitSelector = Selector.GetHandle("init");
		static readonly IntPtr InitWithDisplaySelector = Selector.GetHandle("initWithDisplay:excludingWindows:");
		static readonly IntPtr SetWidthSelector = Selector.GetHandle("setWidth:");
		static readonly IntPtr SetHeightSelector = Selector.GetHandle("setHeight:");
		static readonly IntPtr SetSourceRectSelector = Selector.GetHandle("setSourceRect:");
		static readonly IntPtr SetShowsCursorSelector = Selector.GetHandle("setShowsCursor:");
		static readonly IntPtr RetainSelector = Selector.GetHandle("retain");
		static readonly IntPtr ReleaseSelector = Selector.GetHandle("release");

		[DllImport("/System/Library/Frameworks/ApplicationServices.framework/Versions/A/Frameworks/CoreGraphics.framework/CoreGraphics")]
		static extern IntPtr CGDisplayCreateImageForRect(uint display, CGRect rect);

		delegate void CaptureImageCompletion(IntPtr block, IntPtr image, IntPtr error);
		static readonly CaptureImageCompletion CaptureImageCompletionHandler = CaptureImageCompleted;

		public ScreenHandler (NSScreen screen)
		{
			this.Control = screen;
		}

		public float RealScale => (float)Control.BackingScaleFactor;

		public float Scale => 1f;

		public RectangleF Bounds
		{
			get
			{ 
				var bounds = Control.Frame;
				var origin = NSScreen.Screens[0].Frame.Bottom;
				bounds.Y = origin - bounds.Height - bounds.Y;
				return bounds.ToEto();

			}
		}

		public RectangleF WorkingArea
		{
			get
			{ 
				var workingArea = Control.VisibleFrame;
				var origin = NSScreen.Screens[0].Frame.Bottom;
				workingArea.Y = origin - workingArea.Height - workingArea.Y;
				return workingArea.ToEto();
			}
		}

		public int BitsPerPixel
		{
			get { return (int)NSGraphics.BitsPerPixelFromDepth (Control.Depth); }
		}

		public bool IsPrimary
		{
			get { return Control == NSScreen.Screens[0]; }
		}

		public Image GetImage(RectangleF rect)
		{
			Image image = null;
			if (MacVersion.IsAtLeast(15, 2))
				image = CaptureImageInRect(rect);
			else if (MacVersion.IsAtLeast(14, 0))
				image = CaptureImageWithFilter(rect);

			return image ?? CaptureImageWithCoreGraphics(rect);
		}

#if MONOMAC
		[MonoMac.MonoPInvokeCallback(typeof(CaptureImageCompletion))]
#else
		[MonoPInvokeCallback(typeof(CaptureImageCompletion))]
#endif
		static unsafe void CaptureImageCompleted(IntPtr block, IntPtr image, IntPtr error)
		{
			var descriptor = (BlockLiteral*)block;
			var callback = (Action<IntPtr, IntPtr>)descriptor->Target;
			callback?.Invoke(image, error);
		}

		Bitmap CaptureImageWithFilter(RectangleF rect)
		{
			if (ScreenCaptureKitHandle == IntPtr.Zero
				|| ScreenshotManagerClass == IntPtr.Zero
				|| ShareableContentClass == IntPtr.Zero
				|| ContentFilterClass == IntPtr.Zero
				|| StreamConfigurationClass == IntPtr.Zero
				|| Control.DeviceDescription["NSScreenNumber"] is not NSNumber id)
				return null;

			var content = GetShareableContent();
			if (content == IntPtr.Zero)
				return null;

			IntPtr filter = IntPtr.Zero;
			IntPtr configuration = IntPtr.Zero;

			try
			{
				var display = FindDisplay(content, id.UInt32Value);
				if (display == IntPtr.Zero)
					return null;

				using var excludedWindows = NSArray.FromNSObjects(Array.Empty<NSObject>());
				var allocatedFilter = Messaging.IntPtr_objc_msgSend(ContentFilterClass, AllocSelector);
				filter = Messaging.IntPtr_objc_msgSend_IntPtr_IntPtr(allocatedFilter, InitWithDisplaySelector, display, excludedWindows.Handle);
				if (filter == IntPtr.Zero)
					return null;

				var allocatedConfiguration = Messaging.IntPtr_objc_msgSend(StreamConfigurationClass, AllocSelector);
				configuration = Messaging.IntPtr_objc_msgSend(allocatedConfiguration, InitSelector);
				if (configuration == IntPtr.Zero)
					return null;

				var width = (nuint)Math.Max(1, Math.Ceiling(rect.Width * RealScale));
				var height = (nuint)Math.Max(1, Math.Ceiling(rect.Height * RealScale));
				Messaging.void_objc_msgSend_nuint(configuration, SetWidthSelector, width);
				Messaging.void_objc_msgSend_nuint(configuration, SetHeightSelector, height);
				// SourceRect is relative to the display selected by the content filter.
				Messaging.void_objc_msgSend_CGRect(configuration, SetSourceRectSelector, rect.ToNS());
				Messaging.void_objc_msgSend_bool(configuration, SetShowsCursorSelector, false);

				return CaptureImage(
					block => Messaging.void_objc_msgSend_IntPtr_IntPtr_IntPtr(
						ScreenshotManagerClass,
						CaptureImageWithFilterSelector,
						filter,
						configuration,
						block));
			}
			finally
			{
				if (configuration != IntPtr.Zero)
					Messaging.void_objc_msgSend(configuration, ReleaseSelector);
				if (filter != IntPtr.Zero)
					Messaging.void_objc_msgSend(filter, ReleaseSelector);
				Messaging.void_objc_msgSend(content, ReleaseSelector);
			}
		}

		Bitmap CaptureImageInRect(RectangleF rect)
		{
			if (ScreenCaptureKitHandle == IntPtr.Zero || ScreenshotManagerClass == IntPtr.Zero)
				return null;

			// captureImageInRect uses global screen coordinates.
			rect.Location += Bounds.Location;
			return CaptureImage(
				block => Messaging.void_objc_msgSend_CGRect_IntPtr(
					ScreenshotManagerClass,
					CaptureImageInRectSelector,
					rect.ToNS(),
					block));
		}

		Image CaptureImageWithCoreGraphics(RectangleF rect)
		{
			if (Control.DeviceDescription["NSScreenNumber"] is not NSNumber id)
				return null;

			var pixelRect = rect * Widget.LogicalPixelSize;
			var cgimagePtr = CGDisplayCreateImageForRect(id.UInt32Value, pixelRect.ToNS());
#if MACOS_NET
			using var cgimage = Runtime.GetINativeObject<CGImage>(cgimagePtr, true);
#else
			using var cgimage = cgimagePtr == IntPtr.Zero ? null : new CGImage(cgimagePtr);
#endif
			// Preserve the physical pixels when accessing the image as a bitmap.
			return cgimage == null ? null : new Icon(new IconHandler(new NSImage(cgimage, new CGSize(cgimage.Width, cgimage.Height))));
		}

		static unsafe Bitmap CaptureImage(Action<IntPtr> invoke)
		{
			Bitmap bitmap = null;
			using var completed = new ManualResetEventSlim();
			Action<IntPtr, IntPtr> callback = (image, error) =>
			{
				try
				{
					if (image != IntPtr.Zero && error == IntPtr.Zero)
					{
#if MACOS_NET
						using var cgimage = Runtime.GetINativeObject<CGImage>(image, false);
#else
						using var cgimage = new CGImage(image);
#endif
						// Preserve the physical pixels when accessing the image as a bitmap.
						var imageSize = new CGSize(cgimage.Width, cgimage.Height);
						bitmap = new Bitmap(new BitmapHandler(new NSImage(cgimage, imageSize)));
					}
				}
				catch
				{
					bitmap = null;
				}
				finally
				{
					completed.Set();
				}
			};

			BlockLiteral block = new BlockLiteral();
			block.SetupBlock(CaptureImageCompletionHandler, callback);

			try
			{
				invoke((IntPtr)(&block));
				completed.Wait();
			}
			finally
			{
				block.CleanupBlock();
			}

			return bitmap;
		}

		static unsafe IntPtr GetShareableContent()
		{
			IntPtr content = IntPtr.Zero;
			using var completed = new ManualResetEventSlim();
			Action<IntPtr, IntPtr> callback = (result, error) =>
			{
				try
				{
					if (result != IntPtr.Zero && error == IntPtr.Zero)
					{
						Messaging.void_objc_msgSend(result, RetainSelector);
						content = result;
					}
				}
				catch
				{
					content = IntPtr.Zero;
				}
				finally
				{
					completed.Set();
				}
			};

			BlockLiteral block = new BlockLiteral();
			block.SetupBlock(CaptureImageCompletionHandler, callback);

			try
			{
				Messaging.void_objc_msgSend_IntPtr(ShareableContentClass, GetShareableContentSelector, (IntPtr)(&block));
				completed.Wait();
			}
			finally
			{
				block.CleanupBlock();
			}

			return content;
		}

		static IntPtr FindDisplay(IntPtr content, uint displayId)
		{
			var displays = Messaging.IntPtr_objc_msgSend(content, DisplaysSelector);
			if (displays == IntPtr.Zero)
				return IntPtr.Zero;

			var count = Messaging.nuint_objc_msgSend(displays, CountSelector);

			for (nuint index = 0; index < count; index++)
			{
				var display = Messaging.IntPtr_objc_msgSend_nuint(displays, ObjectAtIndexSelector, index);
				if (display != IntPtr.Zero && Messaging.uint_objc_msgSend(display, DisplayIdSelector) == displayId)
					return display;
			}

			return IntPtr.Zero;
		}
	}
}
