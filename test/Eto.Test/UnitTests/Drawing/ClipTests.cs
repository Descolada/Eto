using NUnit.Framework;

namespace Eto.Test.UnitTests.Drawing
{
	[TestFixture]
	public class ClipTests
	{
		[Test]
		public void ClipBoundsShouldMatchClientSize()
		{
			var size = new Size(300, 300);
			TestBase.Paint((drawable, e) =>
			{
				var graphics = e.Graphics;
				Assert.That(drawable.ClientSize, Is.EqualTo(size), "Drawable client size should be 300x300");
				Assert.That(Size.Round(graphics.ClipBounds.Size), Is.EqualTo(Size.Round(drawable.ClientSize)), "Clip bounds should match drawable client size");
			}, size);
		}

		[Test]
		public void ClipRectangleShouldTranslate()
		{
			TestBase.Paint((drawable, e) =>
			{
				var graphics = e.Graphics;
				// Clip to the upper-left quadrant
				var clipTo = drawable.ClientSize / 2;
				graphics.SetClip(new RectangleF(PointF.Empty, clipTo));

				// Translate to the bottom-right quadrant
				graphics.TranslateTransform(new Point(clipTo));

				// Check that the clip region was correctly translated
				var clip = graphics.ClipBounds;
				var expectedClip = new RectangleF(-new Point(clipTo), clipTo);
				Assert.That(Rectangle.Round(clip), Is.EqualTo(Rectangle.Round(expectedClip)), "Clip rectangle wasn't translated properly");
			});
		}

		[Test]
		public void IntersectClipShouldIntersectPathsAndReset()
		{
			using var bitmap = new Bitmap(100, 60, PixelFormat.Format32bppRgba);

			using (var graphics = new Graphics(bitmap))
			{
				graphics.AntiAlias = false;
				graphics.Clear(Colors.Transparent);

				using (var basePath = new GraphicsPath())
				{
					basePath.AddEllipse(10, 10, 80, 40);
					graphics.SetClip(basePath);
				}

				using (var windingPath = new GraphicsPath { FillMode = FillMode.Winding })
				{
					windingPath.AddRectangle(10, 10, 45, 40);
					windingPath.AddRectangle(30, 18, 15, 20);
					graphics.IntersectClip(windingPath);
				}

				using (var topPath = new GraphicsPath())
				{
					topPath.AddRectangle(0, 0, 100, 28);
					graphics.IntersectClip(topPath);
				}

				graphics.AntiAlias = true;
				graphics.FillRectangle(Colors.Blue, 0, 0, 100, 60);

				graphics.ResetClip();
				graphics.FillRectangle(Colors.Red, 94, 54, 4, 4);
			}

			using var data = bitmap.Lock();
			Assert.That(data.GetPixel(25, 18), Is.EqualTo(Colors.Blue), "#1");
			Assert.That(data.GetPixel(35, 22), Is.EqualTo(Colors.Blue), "#2");
			Assert.That(data.GetPixel(70, 15), Is.EqualTo(Colors.Transparent), "#3");
			Assert.That(data.GetPixel(20, 40), Is.EqualTo(Colors.Transparent), "#4");
			Assert.That(data.GetPixel(15, 15), Is.EqualTo(Colors.Transparent), "#5");
			Assert.That(data.GetPixel(95, 55), Is.EqualTo(Colors.Red), "#6");
		}

		[Test]
		public void IntersectClipShouldUseTransformAtTimeOfCall()
		{
			using var bitmap = new Bitmap(50, 40, PixelFormat.Format32bppRgba);

			using (var graphics = new Graphics(bitmap))
			{
				graphics.AntiAlias = false;
				graphics.Clear(Colors.Transparent);
				graphics.SaveTransform();
				graphics.TranslateTransform(12, 8);

				using (var path = new GraphicsPath())
				{
					path.AddRectangle(0, 0, 20, 16);
					graphics.IntersectClip(path);
				}

				graphics.RestoreTransform();
				graphics.FillRectangle(Colors.Blue, 0, 0, 50, 40);
			}

			using var data = bitmap.Lock();
			Assert.That(data.GetPixel(15, 10), Is.EqualTo(Colors.Blue), "#1");
			Assert.That(data.GetPixel(5, 10), Is.EqualTo(Colors.Transparent), "#2");
			Assert.That(data.GetPixel(35, 10), Is.EqualTo(Colors.Transparent), "#3");
			Assert.That(data.GetPixel(15, 30), Is.EqualTo(Colors.Transparent), "#4");
		}
	}
}
