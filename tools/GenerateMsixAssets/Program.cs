using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: GenerateMsixAssets <output-dir>");
    Environment.Exit(2);
}

var outDir = args[0];
Directory.CreateDirectory(outDir);

var sizes = new (string name, int w, int h)[] {
    ("Square44x44Logo", 44, 44),
    ("Square71x71Logo", 71, 71),
    ("Square150x150Logo", 150, 150),
    ("Wide310x150Logo", 310, 150),
    ("Square310x310Logo", 310, 310),
    ("StoreLogo", 50, 50),
};

var bg = Color.FromRgb(32, 96, 160); // brand color

foreach (var (name, w, h) in sizes)
{
    using var img = new Image<Rgba32>(w, h, bg);
    img.Mutate(ctx => ctx.Fill(bg));
    var path = Path.Combine(outDir, name + ".png");
    img.SaveAsPng(path);
    Console.WriteLine($"Generated {path}");
}