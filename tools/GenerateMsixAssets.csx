#r "nuget: SixLabors.ImageSharp, 3.1.4"
#r "nuget: SixLabors.Fonts, 1.0.0"
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;
using System.IO;

var outDir = args.Length > 0 ? args[0] : @"src/Installer.Msix/Assets";
Directory.CreateDirectory(outDir);

var sizes = new (string name, int w, int h)[] {
    ("Square44x44Logo", 44, 44),
    ("Square71x71Logo", 71, 71),
    ("Square150x150Logo", 150, 150),
    ("Wide310x150Logo", 310, 150),
    ("Square310x310Logo", 310, 310),
    ("StoreLogo", 50, 50),
};

var bg = Color.FromRgb(32, 96, 160); // brand-ish blue
var fg = Color.White;

var fontCollection = new FontCollection();
var font = SystemFonts.CreateFont("Segoe UI", 36, FontStyle.Bold);

foreach (var (name, w, h) in sizes)
{
    using var img = new Image<Rgba32>(w, h, bg);
    var text = "KI";
    img.Mutate(ctx => {
        ctx.Fill(bg);
        var options = new TextGraphicsOptions(){ HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        ctx.DrawText(options, text, font, fg, new PointF(w/2f, h/2f));
    });
    var path = Path.Combine(outDir, name + ".png");
    img.SaveAsPng(path);
    Console.WriteLine($"Generated {path}");
}