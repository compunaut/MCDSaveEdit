using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using PakReader.Pak;
using PakReader.Parsers.Objects;
using SkiaSharp;

namespace MCDSaveEdit.Services
{
    /// <summary>Explicit CI-only smoke mode for the actual published executable.</summary>
    public static class NativeVerification
    {
        public static int Run(string reportPath)
        {
            try
            {
                var method = typeof(PakFileReader).Assembly.GetType("PakReader.TextureDecoder", true)!
                    .GetMethod("DecodeImage", BindingFlags.Public | BindingFlags.Static)!;
                byte[] pixels = { 3, 2, 1, 255 };
                using var image = (SKImage)method.Invoke(null, new object[] { pixels, 1, 1, 1, EPixelFormat.PF_B8G8R8A8 })!;
                Array.Clear(pixels);
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                using var bitmap = SKBitmap.FromImage(image);
                if (bitmap.GetPixel(0, 0) != new SKColor(1, 2, 3, 255))
                    throw new InvalidOperationException("Native texture pixel verification failed.");
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                if (encoded == null || encoded.Size == 0) throw new InvalidOperationException("Native PNG encoding failed.");
                using var process = Process.GetCurrentProcess();
                string[] loaded = process.Modules.Cast<ProcessModule>()
                    .Where(module => string.Equals(module.ModuleName, "libSkiaSharp.dll", StringComparison.OrdinalIgnoreCase))
                    .Select(module => Path.GetFullPath(module.FileName)).ToArray();
                string expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "libSkiaSharp.dll"));
                if (loaded.Length != 1 || !string.Equals(loaded[0], expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Skia was not loaded from the validated publish directory.");
                File.WriteAllText(reportPath, JsonSerializer.Serialize(new {
                    success = true, architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                    skiaAssembly = typeof(SKImage).Assembly.GetName().Version!.ToString(), nativePath = loaded[0], pngBytes = encoded.Size
                }, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(reportPath, JsonSerializer.Serialize(new { success = false, error = error.Message }));
                return 1;
            }
        }
    }
}
