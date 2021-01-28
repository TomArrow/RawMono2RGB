using ImageMagick;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tests
{
    class Program
    {
        static void Main(string[] args)
        {
            // Jan05B_BttF2_R1_018587.exr (corrupt file)
            // Read from file
            string corruptFile = "Jan05B_BttF2_R1_018587.exr";
            string goodFile = "Jan05B_BttF2_R1_018586.exr";

            //readImageInfo(corruptFile);
            //readImageInfo(goodFile);
            readImage(corruptFile);
            readImage(goodFile);

            Console.ReadKey();
        }

        private static void readImageInfo(string fileName)
        {
            var info = new MagickImageInfo(fileName);

            // Read from stream
            using (var memStream = LoadMemoryStreamImage(fileName))
            {
                info = new MagickImageInfo(memStream);
            }

            // Read from byte array
            var data = LoadImageBytes(fileName);
            info = new MagickImageInfo(data);

            info = new MagickImageInfo();
            info.Read(fileName);
            using (var memStream = LoadMemoryStreamImage(fileName))
            {
                info.Read(memStream);
            }
            info.Read(data);

            Console.WriteLine(info.Width);
            Console.WriteLine(info.Height);
            Console.WriteLine(info.ColorSpace);
            Console.WriteLine(info.Format);
            Console.WriteLine(info.Density.X);
            Console.WriteLine(info.Density.Y);
            Console.WriteLine(info.Density.Units);
        }
        
        private static void readImage(string fileName)
        {
            var settings = new MagickReadSettings();
            settings.Debug = true;
            settings.Verbose = true;

            // Read from byte array.
            var data = LoadImageBytes(fileName);
            using (var image = new MagickImage(data, settings))
            {

                Console.WriteLine(image.Gamma);
                image.Depth = 16;
                image.ColorSpace = ColorSpace.Undefined;
                byte[] blah = image.ToByteArray(MagickFormat.Rgb);
                //image.Write(fileName+"-resave.exr");
                File.WriteAllBytes(fileName+"-rawbytes.raw",blah);
            }

        }

        private static MemoryStream LoadMemoryStreamImage(string image)
        {
            return new MemoryStream(LoadImageBytes(image));
        }

        private static byte[] LoadImageBytes(string image)
        {
            return File.ReadAllBytes(image);
        }
    }
}
