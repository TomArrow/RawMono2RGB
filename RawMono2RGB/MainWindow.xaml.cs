using BitMiracle.LibTiff.Classic;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Shapes = System.Windows.Shapes;
using Forms = System.Windows.Forms;
using System.Numerics;

using System.Drawing;
using Imaging = System.Drawing.Imaging;
using Orientation = BitMiracle.LibTiff.Classic.Orientation;
using System.Runtime.InteropServices;
using System.Threading;
using ImageMagick;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;

namespace RawMono2RGB
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window,  INotifyPropertyChanged
    {
        
        private const TiffTag TIFFTAG_CFAREPEATPATTERNDIM = (TiffTag)33421;
        private const TiffTag TIFFTAG_CFAPATTERN = (TiffTag)33422;

        //private static Tiff.TiffExtendProc m_parentExtender;
        private BackgroundWorker worker = new BackgroundWorker();

        string sourceFolder = null;
        string targetFolder = null;
        string[] filesInSourceFolder = null;
        private int currentProgress;
        private string currentStatus;
        private static int _counterTotal = 0;
        private static int _counterDone = 0;
        private static int _counterSkippedExisting = 0;
        private static int _counterSkippedRange = 0;
        private static int _totalFiles = 0;

        // Declare the event
        public event PropertyChangedEventHandler PropertyChanged;


        // Create the OnPropertyChanged method to raise the event
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }

        public MainWindow()
        {

            //new ColorManager.ICC.ChromaticityTagDataEntry();


            InitializeComponent();
        }


        private enum FORMAT { MONO16, MONO12p };


        private enum CHANNELCOLOR { RED, GREEN, BLUE };

        private struct ShotSetting
        {
            public int orderIndex;
            public CHANNELCOLOR channelColor;
            public float exposureMultiplier;
        }

        static Regex shotSettingTextRegexp = new Regex(@"(R|G|B|X)(?:(\+|\-|\*|\/)(\d+))?", RegexOptions.IgnoreCase);



        private float getHDRClippingpoint()
        {
            float clippingPoint = 0.99f;
            float.TryParse(clippingPoint_txt.Text, out clippingPoint);
            return clippingPoint;
        }

        private double getFeatherStops()
        {
            double featherStops = 0;
            double.TryParse(featherStops_txt.Text, out featherStops);
            return featherStops;
        }

        private float getFeatherMultiplier()
        {
            return (float)Math.Pow(2, -getFeatherStops());
        }

        private ShotSetting[] getShots()
        {
            string[] shotTexts = { colorA.Text, colorB.Text, colorC.Text, colorD.Text, colorE.Text, colorF.Text };
            List<ShotSetting> shotSettings = new List<ShotSetting>();

            int index = 0;

            for(int i=0; i<6; i++)
            {

                if(shotTexts[i].Trim() == "")
                {
                    // nothing
                }
                else
                {
                    ShotSetting shotSettingTemp = new ShotSetting();
                    shotSettingTemp.exposureMultiplier = 1;
                    shotSettingTemp.orderIndex = index;

                    MatchCollection matches = shotSettingTextRegexp.Matches(shotTexts[i]);

                    // Try cach just in case the regexp doesnt give proper results for some reason.
                    try
                    {
                        string color = matches[0].Groups[1].Value.ToUpper();
                        string operater = matches[0].Groups[2].Value;
                        string number = matches[0].Groups[3].Value;

                        float numberParsed= 1;
                        bool numberParsingSuccessful = float.TryParse(number, out numberParsed);

                        bool isEmpty = false;

                        switch (color)
                        {
                            case "R":
                                shotSettingTemp.channelColor = CHANNELCOLOR.RED;
                                break;
                            case "G":
                                shotSettingTemp.channelColor = CHANNELCOLOR.GREEN;
                                break;
                            case "B":
                                shotSettingTemp.channelColor = CHANNELCOLOR.BLUE;
                                break;
                            case "X":
                                isEmpty = true;
                                break;
                            default:
                                break;
                        }

                        // If the number wasn't parsed properly, may as well stop here.
                        if (!isEmpty && numberParsingSuccessful)
                        {
                            switch (operater)
                            {
                                case "+":
                                    shotSettingTemp.exposureMultiplier = (float)Math.Pow(2, numberParsed);
                                    break;
                                case "*":
                                    shotSettingTemp.exposureMultiplier = numberParsed;
                                    break;

                                case "-":
                                case "/":
                                default:
                                    // Not implemented. Always declare the overexposed shots!
                                    shotSettingTemp.exposureMultiplier = 1;
                                    break;
                            }
                        }

                        if (!isEmpty)
                        {
                            shotSettings.Add(shotSettingTemp);
                            index++;
                        }
                        else
                        {

                            // nothing
                        }
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show("Some weird error parsing the shot settings.");
                    }
                }


            }

            // Order by color and exposure
            // Color order is primary order: R, G, B
            // Exposure order: Starting with smallest exposure multiplier.
            shotSettings.Sort(OrderComparisonTwoShotSettings);

            return shotSettings.ToArray();
        }
        
        static int OrderComparisonTwoShotSettings(ShotSetting shotSetting1, ShotSetting shotSetting2)
        {
            int tmpCompare = shotSetting1.channelColor.CompareTo(shotSetting2.channelColor);

            if(tmpCompare != 0)
            {
                return tmpCompare;
            } else
            {
                return shotSetting1.exposureMultiplier.CompareTo(shotSetting2.exposureMultiplier);
            }
        }

        private FORMAT getInputFormat()
        {
            return this.Dispatcher.Invoke(() =>
            {
                FORMAT inputFormat = FORMAT.MONO16;

                if ((bool)formatRadio_mono16.IsChecked)
                {
                    inputFormat = FORMAT.MONO16;
                }
                else if ((bool)formatRadio_mono12p.IsChecked)
                {
                    inputFormat = FORMAT.MONO12p;
                }
                return inputFormat;
            });
        }

        private byte[] convert12pto16bit(byte[] input)
        {
            long inputlength = input.Length * 8;
            long outputLength = inputlength / 12 * 16;
            long inputlengthBytes = inputlength / 8;
            long outputLengthBytes = outputLength / 8;

            byte[] output = new byte[outputLengthBytes];

            // For each 3 bytes in input, we write 4 bytes in output
            for (long i = 0, o = 0; i < inputlengthBytes; i += 3, o += 4)
            {

                output[o + 1] = (byte)((input[i] & 0b1111_0000) >> 4 | ((input[i + 1] & 0b0000_1111) << 4));
                output[o] = (byte)((input[i] & 0b0000_1111) << 4);
                output[o + 3] = (byte)input[i + 2];
                output[o + 2] = (byte)((input[i + 1] & 0b1111_0000));
            }

            return output;
        }


        private enum TARGETFORMAT { TIF,EXR};


        private void ProcessRAW(string[] srcRGBTriplet, ShotSetting[] shotSettings,string targetFilename, TARGETFORMAT targetFormat, FORMAT inputFormat,int maxThreads, float HDRClippingPoint, float HDRFeatherMultiplier,bool EXRIntegrityVerification)
        {

            int groupLength = shotSettings.Length;

            byte[][] buffers = new byte[groupLength][]; 
            for(int i = 0;i < groupLength; i++){

                buffers[i] = File.ReadAllBytes(srcRGBTriplet[i]);
                if (inputFormat == FORMAT.MONO12p)
                {
                    buffers[i] = convert12pto16bit(buffers[i]);
                }
            }
            

            int width = 4096;
            int height = 3000;

            this.Dispatcher.Invoke(() =>
            {
                width = 1;
                int.TryParse(rawWidth.Text,out width);
                height = 1;
                int.TryParse(rawHeight.Text,out height);
            });


            byte[][] RGBBuffers = HDRMerge(buffers, shotSettings,HDRClippingPoint,HDRFeatherMultiplier);

            byte[] buffR = RGBBuffers[0];
            byte[] buffG = RGBBuffers[1];
            byte[] buffB = RGBBuffers[2];

            // Interleave
            int pixelCount = width * height;
            int totalLength = width * height * 3;
            byte[] buff = new byte[totalLength*2];


            if (buffR.Count() < pixelCount * 2)
            {
                this.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("Red file too short: "+ srcRGBTriplet[0]);
                });
                return;
            }
            if (buffG.Count() < pixelCount * 2)
            {
                this.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("Green file too short: " + srcRGBTriplet[1]);
                });
                return;
            }
            if (buffB.Count() < pixelCount * 2)
            {
                this.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("Blue file too short: " + srcRGBTriplet[2]);
                });
                return;
            }

            for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                /*
                // BGR
                buff[pixelIndex * 3 * 2] = buffB[pixelIndex * 2];
                buff[pixelIndex * 3 * 2 + 1] = buffB[pixelIndex * 2 + 1];
                buff[pixelIndex * 3 * 2 +4] = buffR[pixelIndex*2];
                buff[pixelIndex * 3 * 2 +5] = buffR[pixelIndex * 2 + 1];
                buff[pixelIndex * 3 * 2 +2] = buffG[pixelIndex * 2];
                buff[pixelIndex * 3 * 2 +3] = buffG[pixelIndex * 2 + 1];
                */
                // RGB
                buff[pixelIndex * 3 * 2] = buffR[pixelIndex * 2];
                buff[pixelIndex * 3 * 2 + 1] = buffR[pixelIndex * 2 + 1];
                buff[pixelIndex * 3 * 2 + 2] = buffG[pixelIndex * 2];
                buff[pixelIndex * 3 * 2 + 3] = buffG[pixelIndex * 2 + 1];
                buff[pixelIndex * 3 * 2 + 4] = buffB[pixelIndex * 2];
                buff[pixelIndex * 3 * 2 + 5] = buffB[pixelIndex * 2 + 1];
                
            }
            


            string fileName = targetFilename;

            if(targetFormat == TARGETFORMAT.EXR)
            {
                ResourceLimits.Thread = (ulong)maxThreads;
                ResourceLimits.LimitMemory(new Percentage(90));

                MagickReadSettings settings = new MagickReadSettings();
                settings.Width = width;
                settings.Height = height;
                settings.Format = MagickFormat.Rgb; // Correction, this is actually right, I had flipped RGB to BGR elsewhere in the code before. Fixed now.

                /*ColorManager.ICC.ICCProfileWriter iccWriter = new ColorManager.ICC.ICCProfileWriter();
                iccWriter.WriteProfile(new ColorManager.ICC.ICCProfile());
                */

                if (EXRIntegrityVerification)
                {
                    /*
                     * Info on half float format: https://www.openexr.com/about.html
                     */
                    // What does this mean for precision of converting 16 bit integers to 16 bit floating point?
                    // We need to know the maximum precision achievable to be able to tell rounding errors from actual integrity fails.
                    // More info here: https://en.wikipedia.org/wiki/Half-precision_floating-point_format
                    // Basically, precision at any given value is 11 bits or 2048 values.



                    bool integrityCheckPassed = false;
                    while (!integrityCheckPassed)
                    {

                        using (var image = new MagickImage(buff, settings))
                        {
                            //ExifProfile profile = new ExifProfile();
                            //profile.SetValue(ExifTag.UserComment, Encoding.ASCII.GetBytes(srcRGBTriplet[0] + "," + srcRGBTriplet[1] + "," + srcRGBTriplet[2]));
                            //image.SetProfile(profile);
                            image.Format = MagickFormat.Exr;
                            image.Settings.Compression = CompressionMethod.Piz;

                            //image.Write(fileName);

                            byte[] exrFile = image.ToByteArray();

                            bool integrityCheckFailed = false;
                            using (var reloadedImage = new MagickImage(exrFile))
                            {

                                reloadedImage.Depth = 16;
                                reloadedImage.ColorSpace = ColorSpace.Undefined;
                                byte[] reloadedImageBytes = reloadedImage.ToByteArray(MagickFormat.Rgb);

                                integrityCheckFailed = integrityCheckFailed | !IntegrityChecker.VerifyIntegrityUInt16InHalfPrecisionFloat(buff,reloadedImageBytes);
                            }
                            if (integrityCheckFailed)
                            {
                                continue;
                            } else
                            {
                                integrityCheckPassed = true;
                                File.WriteAllBytes(fileName, exrFile);
                            }


                        }
                    }
                } else
                {
                    using (var image = new MagickImage(buff, settings))
                    {
                        //ExifProfile profile = new ExifProfile();
                        //profile.SetValue(ExifTag.UserComment, Encoding.ASCII.GetBytes(srcRGBTriplet[0] + "," + srcRGBTriplet[1] + "," + srcRGBTriplet[2]));
                        //image.SetProfile(profile);
                        image.Format = MagickFormat.Exr;
                        image.Settings.Compression = CompressionMethod.Piz;

                        //image.Write(fileName);
                        byte[] exrFile = image.ToByteArray();
                        File.WriteAllBytes(fileName, exrFile);

                    }
                }

                
            }
            else if (targetFormat == TARGETFORMAT.TIF)
            {

                using (Tiff output = Tiff.Open(fileName, "w"))
                {

                    output.SetField(TiffTag.SUBFILETYPE, 0);
                    //output.SetField(TiffTag.ORIGINALRAWFILENAME, srcRGBTriplet[0]+","+srcRGBTriplet[1]+","+srcRGBTriplet[2]);
                    output.SetField(TiffTag.IMAGEWIDTH, width);
                    output.SetField(TiffTag.IMAGELENGTH, height);
                    output.SetField(TiffTag.SAMPLESPERPIXEL, 3);
                    output.SetField(TiffTag.BITSPERSAMPLE, 16);
                    output.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
                    output.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
                    output.SetField(TiffTag.FILLORDER, FillOrder.MSB2LSB);
                    output.SetField(TiffTag.COMPRESSION, Compression.DEFLATE); 

                    output.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);


                    output.WriteEncodedStrip(0, buff, width * height * 2 * 3);
                }
            }

        }

        static Regex rawFileFrameNumberRegexp = new Regex(@"[^\d](\d+)\.raw$", RegexOptions.IgnoreCase);

        // returns R, G and B buffers with HDR merge already performed.
        // Expects 16 bit linear input.
        private byte[][] HDRMerge(byte[][] buffers, ShotSetting[] shotSettings,float clippingPoint, float featherMultiplier)
        {

            float featherBottomIntensity = clippingPoint;
            float featherRange = 0;
            if(featherMultiplier != 1)
            {
                featherBottomIntensity *= featherMultiplier;
                featherRange = clippingPoint - featherBottomIntensity;
            }

            byte[][] outputBuffers = new byte[3][] { new byte [buffers[0].Length ], new byte[buffers[0].Length], new byte[buffers[0].Length] };

            int singleBufferLength = buffers[0].Length;

            //double maxValue = 0;

            //double Uint16MaxValueDouble = (double)UInt16.MaxValue;
            float Uint16MaxValueFloat = (float)UInt16.MaxValue;


            // Do one color after another
            Parallel.For(0, 3, (colorIndex, state) =>
            //for (var colorIndex = 0; colorIndex < 3; colorIndex++)
            {


                Vector2 Uint16Divider = new Vector2();
                float thisColorMultiplierMultiplier;
                int thisColorIndex;
                ShotSetting thisShotSetting;
                float effectiveMultiplier;
                float currentOutputValue;
                float currentInputValue;
                bool isClipping;
                UInt16 finalValue;
                float inputIntensity;
                float tmpValue;
                byte[] sixteenbitbytes;


                thisColorIndex = 0;
                thisColorMultiplierMultiplier = 1;
                for (var shotSettingIndex = 0; shotSettingIndex < shotSettings.Length; shotSettingIndex++)
                {
                    thisShotSetting = shotSettings[shotSettingIndex];
                    if (colorIndex == (int)thisShotSetting.channelColor)
                    {
                        // first image of each set just has its buffer copied for speed reasons
                        if (thisColorIndex == 0)
                        {
                            outputBuffers[colorIndex] = buffers[thisShotSetting.orderIndex];
                            // The darkest image's multiplier should technically be 1 by default. But if it isn't, we use this to normalize the following images.
                            // For example, if the darkest image multiplier is 2, we record the "multiplier multiplier" as 0.5, as we aren't actually multiplying this image data by 2
                            // and as a result we need to reduce the image multiplier of following images by multiplying it with 0.5.
                            thisColorMultiplierMultiplier = 1 / thisShotSetting.exposureMultiplier;
                        }

                        // Do actual HDR merging
                        else
                        {
                            effectiveMultiplier = thisColorMultiplierMultiplier * thisShotSetting.exposureMultiplier;

                            if (featherMultiplier < 1 && featherRange != 0)
                            {
                                for (var i = 0; i < singleBufferLength; i += 2) // 16 bit steps
                                {
                                    Uint16Divider.X = (float)BitConverter.ToUInt16(outputBuffers[colorIndex], i);
                                    Uint16Divider.Y = (float)BitConverter.ToUInt16(buffers[thisShotSetting.orderIndex], i);


                                    /*currentOutputValue = (double)BitConverter.ToUInt16(outputBuffers[colorIndex], i) / Uint16MaxValueDouble;
                                    currentInputValue = (double)BitConverter.ToUInt16(buffers[thisShotSetting.orderIndex], i) / Uint16MaxValueDouble;*/
                                    Uint16Divider = Vector2.Divide(Uint16Divider, Uint16MaxValueFloat);
                                    currentOutputValue = Uint16Divider.X;
                                    currentInputValue = Uint16Divider.Y;

                                    //if(currentInputValue > maxValue) { maxValue = currentInputValue; }
                                    isClipping = currentInputValue > clippingPoint;
                                    if (!isClipping)
                                    {
                                        finalValue = 0;
                                        if (currentInputValue > featherBottomIntensity)
                                        {
                                            inputIntensity = (featherRange - (clippingPoint - currentInputValue)) / featherRange;
                                            currentInputValue /= effectiveMultiplier;
                                            tmpValue = inputIntensity * currentInputValue + (1 - inputIntensity) * currentOutputValue;
                                            finalValue = (UInt16)Math.Round(tmpValue * Uint16MaxValueFloat);
                                        }
                                        else
                                        {
                                            currentInputValue /= effectiveMultiplier;
                                            finalValue = (UInt16)Math.Round(currentInputValue * Uint16MaxValueFloat);
                                        }

                                        sixteenbitbytes = BitConverter.GetBytes(finalValue);
                                        outputBuffers[colorIndex][i] = sixteenbitbytes[0];
                                        outputBuffers[colorIndex][i + 1] = sixteenbitbytes[1];
                                    }
                                }
                            }
                            else
                            {

                                for (var i = 0; i < singleBufferLength; i += 2) // 16 bit steps
                                {
                                    // Comments:
                                    // You might want to use Buffer.BlockCopy to convert the array from raw bytes to unsigned shorts
                                    // https://markheath.net/post/how-to-convert-byte-to-short-or-float
                                    // Or: Span<ushort> a = MemoryMarshal.Cast<byte, ushort>(data)
                                    // https://markheath.net/post/span-t-audio
                                    Uint16Divider.X = (float)BitConverter.ToUInt16(outputBuffers[colorIndex], i);
                                    Uint16Divider.Y = (float)BitConverter.ToUInt16(buffers[thisShotSetting.orderIndex], i);


                                    /*currentOutputValue = (double)BitConverter.ToUInt16(outputBuffers[colorIndex], i) / Uint16MaxValueDouble;
                                    currentInputValue = (double)BitConverter.ToUInt16(buffers[thisShotSetting.orderIndex], i) / Uint16MaxValueDouble;*/
                                    Uint16Divider = Vector2.Divide(Uint16Divider, Uint16MaxValueFloat);
                                    currentOutputValue = Uint16Divider.X;
                                    currentInputValue = Uint16Divider.Y;

                                    //if (currentInputValue > maxValue) { maxValue = currentInputValue; }
                                    isClipping = currentInputValue > clippingPoint;
                                    if (!isClipping)
                                    {
                                        finalValue = 0;
                                        currentInputValue /= effectiveMultiplier;
                                        finalValue = (UInt16)Math.Round(currentInputValue * Uint16MaxValueFloat);

                                        sixteenbitbytes = BitConverter.GetBytes(finalValue);
                                        outputBuffers[colorIndex][i] = sixteenbitbytes[0];
                                        outputBuffers[colorIndex][i + 1] = sixteenbitbytes[1];
                                    }
                                }
                            }

                        }
                        thisColorIndex++;
                    }
                }




            });

            //MessageBox.Show(maxValue.ToString());

            return outputBuffers;
        }

        static int OrderComparisonTwoRawFiles(string file1,string file2)
        {
            MatchCollection matches1 = rawFileFrameNumberRegexp.Matches(file1);
            MatchCollection matches2 = rawFileFrameNumberRegexp.Matches(file2);

            int value1 = 0;
            int value2 = 0;
            // Try cach just in case the regexp doesnt give proper results for some reason.
            try
            {
                value1 = int.Parse(matches1[0].Groups[1].Value);
                value2 = int.Parse(matches2[0].Groups[1].Value);
            } catch(Exception e)
            {
                // Blah
            }

            return value1.CompareTo(value2);
        }

        private void BtnLoadRAWFolder_Click(object sender, RoutedEventArgs e)
        {

            ShotSetting[] shotSettings = getShots();
            int groupLength = shotSettings.Length;

            var fbd = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
            bool? result = fbd.ShowDialog();

            if (result == true && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                sourceFolder = fbd.SelectedPath;
                txtSrcFolder.Text = sourceFolder;
                if(targetFolder == null)
                {
                    targetFolder = sourceFolder;
                    txtTargetFolder.Text = targetFolder;
                }
                filesInSourceFolder = Directory.GetFiles(fbd.SelectedPath,"*.raw");

                //Sorting
                Array.Sort(filesInSourceFolder, OrderComparisonTwoRawFiles);


                currentImagNumber.Text = "1";
                int groupCount = (int) Math.Floor(filesInSourceFolder.Count() / (double)groupLength);
                totalImageCount.Text = groupCount.ToString();
                processToMax_txt.Text = "("+ groupCount.ToString()+")";
                processFrom_txt.Text = "1";
                processTo_txt.Text = groupCount.ToString();
                if (filesInSourceFolder.Count() % groupLength != 0)
                {
                    MessageBox.Show("Warning: The count of .raw files in the folder is not a multiple of the group length ("+groupLength.ToString()+"). Files may be discarded.");
                }
                slide_currentFile.Maximum = (Math.Floor(filesInSourceFolder.Count() / (double)groupLength));
                slide_currentFile.Minimum = 1;
                slide_currentFile.Value = 1;
                btnProcessFolder.IsEnabled = true;
                btnSaveOrderedList.IsEnabled = true;
            }
        }

        private void Slide_currentFile_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

            ReDrawPreview();
        }


        /*
        // Order of colors
        private byte[] getColorOrder()
        {
            return this.Dispatcher.Invoke(() =>
            {
                //0=Red, 1=Green,   2=Blue
                byte colorAval = 0;
                byte colorBval = 1;
                byte colorCval = 2;
                byte.TryParse(colorA.Text, out colorAval);
                byte.TryParse(colorB.Text, out colorBval);
                byte.TryParse(colorC.Text, out colorCval);
                byte[] colorOrder = { colorAval, colorBval, colorCval };
                return colorOrder;
            });
        }
        
        // Similar purpose as getColorOrder,
        // but the returned array's index is the 3 colors (0 red 1 green 2 blue) and the content is the position
        private byte[] getRGBPositions()
        {
            byte[] colorOrder = getColorOrder();
            byte[] RGBPositions = new byte[3];
            for(byte i =0;i<3;i++)
            {
                switch (colorOrder[i])
                {
                    case 0:
                        RGBPositions[0] = i;
                        break;
                    case 1:
                        RGBPositions[1] = i;
                        break;
                    case 2:
                        RGBPositions[2] = i;
                        break;
                    default:
                        // User entered some garbage!
                        break;
                }
            }
            return RGBPositions;
        }*/

        private void ReDrawPreview()
        {
            if(sourceFolder == null || filesInSourceFolder == null)
            {
                return; // Nothing to do here
            }

            int width = 1, height = 1;
            int.TryParse(rawWidth.Text, out width);
            int.TryParse(rawHeight.Text, out height);

            ShotSetting[] shotSettings = getShots();
            int groupLength = shotSettings.Length;


            //bool doPreviewDebayer = (bool)previewDebayer.IsChecked;
            bool doPreviewGamma = (bool)previewGamma.IsChecked;

            float HDRClippingPoint = getHDRClippingpoint();
            float HDRFeatherMultiplier = getFeatherMultiplier();

            int frameDelay = 0;
            int.TryParse(delay.Text, out frameDelay);
            //byte[] RGBPositions = getRGBPositions();

            int sliderNumber = (int)slide_currentFile.Value;

            double previewExposureMultiplier = Math.Pow(2,previewExposure_slider.Value);

            int baseIndex = (sliderNumber - 1)*groupLength+ frameDelay;

            if((baseIndex + (groupLength-1)) > (filesInSourceFolder.Length -1))
            {
                MessageBox.Show("Group incomplete.");
                return;
            }

            int[] fileIndizi = new int[groupLength]; //{ baseIndex+RGBPositions[0], baseIndex + RGBPositions[1], baseIndex + RGBPositions[2] };
            string[] files = new string[groupLength];

            string currentFileStringForGUI = "";

            for(int i = 0; i<groupLength;i++)
            {
                fileIndizi[i] = baseIndex + i;
                files[i] = filesInSourceFolder[baseIndex + i];
            }
            for (int i = 0; i < groupLength; i++)
            {
                currentFileStringForGUI += shotSettings[i].channelColor + "*" + shotSettings[i].exposureMultiplier + ": " + files[shotSettings[i].orderIndex] + "\n";
            }

            //string[] RGBFiles = new string[3] { filesInSourceFolder[RGBIndizi[0]], filesInSourceFolder[RGBIndizi[1]] , filesInSourceFolder[RGBIndizi[2]] };

            filesInfo_txt.Text = currentFileStringForGUI;

            FORMAT inputFormat = getInputFormat();

            foreach (string file in files)
            {
                if (!File.Exists(file))
                {
                    MessageBox.Show("weirdo error, apparently file " + file + " (no longer?) exists");
                    return;
                }
            }
            {
                int subsample = 8;
                int.TryParse(previewSubsample_txt.Text, out subsample);

                int newWidth = (int)Math.Ceiling((double)width / subsample);
                int newHeight = (int)Math.Ceiling((double)height / subsample);


                byte[] newbytes;

                {
                    byte[][] buffers = new byte[groupLength][];
                    for (int i = 0; i < groupLength; i++)
                    {
                        buffers[i] = File.ReadAllBytes(files[i]);
                        if (inputFormat == FORMAT.MONO12p)
                        {
                            buffers[i] = convert12pto16bit(buffers[i]);
                        }
                    }

                    int byteDepth = 2; // This is for the source
                    int byteWidth = newWidth * 3; // This is for the preview. 3 means RGB
                    int newStride = Helpers.getStride(byteWidth);


                    byte[][] mergedRGBbuffers = HDRMerge(buffers, shotSettings, HDRClippingPoint, HDRFeatherMultiplier);

                    newbytes = Helpers.DrawPreview(mergedRGBbuffers[0], mergedRGBbuffers[1], mergedRGBbuffers[2], newHeight, newWidth, height, width, newStride, byteDepth, subsample, doPreviewGamma, previewExposureMultiplier);
                }

                // Put preview into WPF image tag
                Bitmap manipulatedImage = new Bitmap(newWidth, newHeight, Imaging.PixelFormat.Format24bppRgb);
                Imaging.BitmapData pixelData = manipulatedImage.LockBits(new Rectangle(0, 0, newWidth, newHeight), Imaging.ImageLockMode.WriteOnly, Imaging.PixelFormat.Format24bppRgb);

                System.Runtime.InteropServices.Marshal.Copy(newbytes, 0, pixelData.Scan0, newbytes.Count());
                
                manipulatedImage.UnlockBits(pixelData);

                System.Runtime.InteropServices.Marshal.Copy(newbytes, 0, pixelData.Scan0, newbytes.Count());
                // Do the displaying
                mainPreview.Source = Helpers.BitmapToImageSource(manipulatedImage);
            }
        }

        private void PreviewGamma_Click(object sender, RoutedEventArgs e)
        {

            ReDrawPreview();
        }
        private void Color_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReDrawPreview();
        }

        private void BtnLoadTargetFolder_Click(object sender, RoutedEventArgs e)
        {
            var fbd = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
            bool? result = fbd.ShowDialog();

            if (result == true && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                targetFolder = fbd.SelectedPath;
                txtTargetFolder.Text = targetFolder;
                txtStatus.Text = "Target fodler set to " + targetFolder;
            }
        }

        private void BtnProcessFolder_Click(object sender, RoutedEventArgs e)
        {
            worker.WorkerReportsProgress = true;
            worker.WorkerSupportsCancellation = true;
            worker.DoWork += worker_DoWork;
            worker.ProgressChanged += worker_ProgressChanged;
            worker.RunWorkerAsync();
        }

        public int CurrentProgress
        {
            get { return currentProgress; }
            private set
            {
                if (currentProgress != value)
                {
                    currentProgress = value;
                    OnPropertyChanged("CurrentProgress");                   
                }
            }
        }

        public string CurrentStatus
        {
            get { return currentStatus; }
            private set
            {
                if (currentStatus != value)
                {
                    currentStatus = value;
                    OnPropertyChanged("CurrentStatus");
                }
            }
        }

        private List<string[]> getTriplets()
        {
            
            List<string[]> completeTriplets = new List<string[]>();
            /*int frameDelay = 0;
            byte[] RGBPositions = new byte[1];
            this.Dispatcher.Invoke(() =>
            {
                frameDelay = 0;
                int.TryParse(delay.Text, out frameDelay);
                RGBPositions = getRGBPositions();
            });

            for (int baseIndex = frameDelay; baseIndex < filesInSourceFolder.Length; baseIndex += 3)
            {

                if ((baseIndex + 2) > (filesInSourceFolder.Length - 1))
                {
                    MessageBox.Show("Triplet incomplete. Skipping."); // TODO make these so they dont have to be clicked or sth
                    continue;
                }

                int[] RGBIndizi = new int[3] { baseIndex + RGBPositions[0], baseIndex + RGBPositions[1], baseIndex + RGBPositions[2] };

                string[] RGBFiles = new string[3] { filesInSourceFolder[RGBIndizi[0]], filesInSourceFolder[RGBIndizi[1]], filesInSourceFolder[RGBIndizi[2]] };

                completeTriplets.Add(RGBFiles);
            }
            */
            return completeTriplets;
        }

        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {

            //_totalFiles = filesInSourceFolder.Length;

            List<string[]> completeGroups = new List<string[]>();

            int frameDelay = 0;
            //byte[] RGBPositions = new byte[1];
            ShotSetting[] shotSettings = new ShotSetting[3];
            bool IsTIFF = false;
            bool IsEXR = false;
            int maxThreads = Environment.ProcessorCount;
            string customOutputName = "";
            int leadingZeros = 0;
            bool overwriteExisting = false;
            float HDRClippingPoint = 0.99f;
            float HDRFeatherMultiplier = 1;
            bool EXRIntegrityVerification = true;
            this.Dispatcher.Invoke(() =>
            {
                frameDelay = 0;
                int.TryParse(delay.Text,out frameDelay);
                int.TryParse(outputNameLeadingZeros_txt.Text,out leadingZeros);
                int.TryParse(maxThreads_txt.Text,out maxThreads);
                //RGBPositions = getRGBPositions();
                shotSettings = getShots();
                IsTIFF = (bool)formatTif.IsChecked;
                IsEXR = (bool)formatExr.IsChecked;
                customOutputName = outputNameBase_txt.Text;
                overwriteExisting = !(bool)overwrite_no.IsChecked && (bool)overwrite_yes.IsChecked;
                HDRClippingPoint = getHDRClippingpoint();
                HDRFeatherMultiplier = getFeatherMultiplier();
                EXRIntegrityVerification = (bool)exrIntegrityVerification_check.IsChecked;
            });

            int groupLength = shotSettings.Length;
            _totalFiles = (int)Math.Floor(filesInSourceFolder.Length / (decimal)groupLength);

            if (maxThreads == 0)
            {
                maxThreads = Environment.ProcessorCount;
            }


            FORMAT inputFormat = getInputFormat();

            TARGETFORMAT targetFormat = TARGETFORMAT.EXR;

            if (IsEXR)
            {

                targetFormat = TARGETFORMAT.EXR;
            } else
            {
                targetFormat = TARGETFORMAT.TIF;
            }


            for (int baseIndex = frameDelay; baseIndex < filesInSourceFolder.Length; baseIndex += groupLength)
            {

                if ((baseIndex + (groupLength-1)) > (filesInSourceFolder.Length - 1))
                {
                    MessageBox.Show("Group incomplete. Skipping.");
                    continue;
                }

                int[] fileIndizi = new int[groupLength];
                string[] files = new string[groupLength];
                for (var i = 0; i < groupLength; i++)
                {
                    fileIndizi[i] = baseIndex + i;
                    files[i] = filesInSourceFolder[baseIndex + i];
                }

                //int[] RGBIndizi = new int[groupLength] { baseIndex + RGBPositions[0], baseIndex + RGBPositions[1], baseIndex + RGBPositions[2] };

                //string[] RGBFiles = new string[groupLength] { filesInSourceFolder[RGBIndizi[0]], filesInSourceFolder[RGBIndizi[1]], filesInSourceFolder[RGBIndizi[2]] };

                completeGroups.Add(files);
            }

            int processFrom = 1;
            int processTo = completeGroups.Count(); 
            this.Dispatcher.Invoke(() =>
            {
                int.TryParse(processFrom_txt.Text, out processFrom);
                int.TryParse(processTo_txt.Text, out processTo);
            });

            // Index starting at 0, but GUI starts at 1
            processFrom--;
            processTo--;


            var countLock = new object();
            CurrentProgress = 0;

            _counterTotal = 0;
            _counterSkippedRange = 0;
            _counterDone = 0;
            _counterSkippedExisting = 0;

            if (EXRIntegrityVerification)
            {
                IntegrityChecker.BuildIntegrityVerificationAcceptableLossCache();
            }

            Parallel.ForEach(completeGroups,
                new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, (currentGroup, loopState,index) =>
                    // foreach (string srcFileName in filesInSourceFolder)
                {

                    _counterTotal++;
                    var percentage = (double)_counterTotal / _totalFiles * 100.0;
                    if (worker.CancellationPending == true)
                    {
                        e.Cancel = true;
                        return;
                    }

                    if(index < processFrom || index > processTo)
                    {
                        // Skip this one.
                        _counterSkippedRange++;
                        lock (countLock) { worker?.ReportProgress((int)percentage); }
                        return;
                    }

                    string fileNameWithoutFolder = customOutputName == "" ? Path.GetFileNameWithoutExtension(currentGroup[0]) : customOutputName + (leadingZeros == 0 ? index.ToString() : index.ToString("D"+leadingZeros.ToString()));

                    string fileNameWithoutExtension =
                        targetFolder + "\\" + fileNameWithoutFolder;
                    string fileName = fileNameWithoutExtension + (targetFormat == TARGETFORMAT.EXR ? ".exr" : "") + (targetFormat == TARGETFORMAT.TIF ? ".tif" : "");


                    if (File.Exists(fileName) && !overwriteExisting)
                    {
                        // Error: File already exists. No overwriting. Move on.
                        //continue;
                        _counterSkippedExisting++;
                        lock (countLock) { worker?.ReportProgress((int)percentage); }
                        return;
                    }

                    ProcessRAW(currentGroup,shotSettings, fileName,targetFormat, inputFormat,maxThreads,HDRClippingPoint,HDRFeatherMultiplier, EXRIntegrityVerification);
                    _counterDone++;
                    lock (countLock) { worker?.ReportProgress((int)percentage); }
                });
            this.Dispatcher.Invoke(() =>
            {
                txtStatus.Text = "Finished";
            });
            
        }

        private void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            // pbStatus.Value = e.ProgressPercentage;
            CurrentProgress = e.ProgressPercentage;
            txtStatus.Text = $"Processed {_counterTotal} out of {_totalFiles}. {_counterDone} successful, {_counterSkippedExisting} skipped (file exists), {_counterSkippedRange} skipped (not in selected range)";
           
            //this.Dispatcher.BeginInvoke(new Action(() => { pbStatus.Value = e.ProgressPercentage; }));
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            worker.CancelAsync();
        }

        private void MagnifierPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ReDrawPreview();
        }

        private void Amplify_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ReDrawPreview();
        }

        private void Delay_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReDrawPreview();
        }


        private void FormatRadio_Checked(object sender, RoutedEventArgs e)
        {
            ReDrawPreview();
        }

        private void PreviewSubsample_txt_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReDrawPreview();

        }

        private void BtnSaveOrderedList_Click(object sender, RoutedEventArgs e)
        {
            /* TODO FIX/ADAPT to HDR mode
            string output = "Ordered file list: \r\n";
            foreach(string filename in filesInSourceFolder)
            {
                output += filename + "\r\n";
            }

            List<string[]> completeTriplets = getTriplets();

            output += "\r\nList of all triplets:\r\n";

            foreach(string[] triplet in completeTriplets)
            {
                output += "Red: " + triplet[0]+"\r\n";
                output += "Green: " + triplet[1]+"\r\n";
                output += "Blue: " + triplet[2]+"\r\n";
                output += "--------------------------------------------------------------------------\r\n";
            }

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Text files (.txt)|*.txt";
            sfd.FileName = "list.txt";
            if (sfd.ShowDialog() == true)
            {
                File.WriteAllText(sfd.FileName, output);
            }
            */
        }

        private void PreviewExposure_txt_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void PreviewExposure_slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ReDrawPreview();
        }
    }
}
